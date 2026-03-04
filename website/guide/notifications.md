# Notifications & Events

IAW agents communicate through three complementary mechanisms: **events** (audit log), **notifications** (pub/sub delivery), and **streams** (real-time Orleans streams). All three are part of the `IAgentV2` interface.

## Events

Events are an immutable audit log stored on each agent grain. Appending an event persists an `AgentEvent` to durable storage and broadcasts it on the `agent-events` Orleans stream.

### Appending Events

```csharp
var agent = grainFactory.GetGrain<IAgentV2>("weather-agent");

await agent.AppendEventAsync(new AgentEvent
{
    Type = "weather.refresh",
    Payload = "Seattle"
});

await agent.AppendEventAsync(new AgentEvent
{
    Type = "weather.alert",
    Payload = "{\"city\":\"Seattle\",\"severity\":\"high\"}",
    Metadata = new() { ["source"] = "weather-api" }
});
```

### Querying Events

```csharp
var events = await agent.QueryEventsAsync(new AgentEventQuery
{
    Type = "weather.alert",
    Limit = 10,
    Descending = true
});

foreach (var e in events)
{
    Console.WriteLine($"{e.TimestampUtc}: {e.Type} - {e.Payload}");
}
```

### AgentEvent

```csharp
[GenerateSerializer]
public sealed class AgentEvent
{
    [Id(0)] public string EventId { get; set; } = Guid.NewGuid().ToString("N");
    [Id(1)] public string Type { get; set; } = string.Empty;
    [Id(2)] public string? Payload { get; set; }
    [Id(3)] public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    [Id(4)] public Dictionary<string, string> Metadata { get; set; } = [];
}
```

### Subscribing to the Event Stream

Every `AppendEventAsync` call also emits the `AgentEvent` on an Orleans stream:

```csharp
var streamProvider = clusterClient.GetStreamProvider("agents");
var stream = streamProvider.GetStream<AgentEvent>(
    StreamId.Create("agent-events", "weather-agent"));

var handle = await stream.SubscribeAsync((entry, _) =>
{
    Console.WriteLine($"Event: {entry.Type} = {entry.Payload}");
    return Task.CompletedTask;
});
```

## Notifications

Notifications provide pub/sub message delivery between agents. An agent subscribes other agents to topics, then publishes messages that are delivered to all subscribers. Each delivery appends a `NotificationRecord` to the subscriber and emits on the `agent-notifications` stream.

### Basic Pub/Sub

```csharp
var publisher = grainFactory.GetGrain<IAgentV2>("publisher");
var subscriber = grainFactory.GetGrain<IAgentV2>("subscriber");

// Register subscriber for a topic on the publisher
await publisher.SubscribeAsync("weather.alert", "subscriber");

// Publish a notification
await publisher.NotifyAsync(new NotificationEnvelope
{
    Topic = "weather.alert",
    Payload = "{\"city\":\"Seattle\",\"severity\":\"high\"}"
});

// Subscriber receives it automatically; read stored notifications
var notifications = await subscriber.QueryNotificationsAsync();
```

### Typed Notifications with NotificationEnvelope

For richer metadata, populate the full `NotificationEnvelope`:

```csharp
await publisher.NotifyAsync(new NotificationEnvelope
{
    Topic = "weather.alert",
    Payload = "{\"city\":\"Seattle\",\"severity\":\"high\"}",
    ContentType = "application/json",
    Schema = "weather.alert",
    SchemaVersion = "1.0",
    MessageId = Guid.NewGuid().ToString("N"),
    CorrelationId = Guid.NewGuid().ToString("N"),
    Headers = new Dictionary<string, string>
    {
        ["source"] = "weather-service",
        ["tenant"] = "alpha"
    }
});
```

### NotificationEnvelope

```csharp
[GenerateSerializer]
public sealed class NotificationEnvelope
{
    [Id(0)] public string Topic { get; set; } = string.Empty;
    [Id(1)] public string Payload { get; set; } = string.Empty;
    [Id(2)] public string ContentType { get; set; } = "application/json";
    [Id(3)] public string? Schema { get; set; }
    [Id(4)] public string? SchemaVersion { get; set; }
    [Id(5)] public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    [Id(6)] public string? CorrelationId { get; set; }
    [Id(7)] public Dictionary<string, string> Headers { get; set; } = [];
    [Id(8)] public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### NotificationRecord

When a notification is delivered to a subscriber:

```csharp
[GenerateSerializer]
public sealed class NotificationRecord
{
    [Id(0)] public string Topic { get; set; } = string.Empty;
    [Id(1)] public string Payload { get; set; } = string.Empty;
    [Id(2)] public DateTimeOffset TimestampUtc { get; set; }
    [Id(3)] public string ContentType { get; set; } = "application/json";
    [Id(4)] public string? Schema { get; set; }
    [Id(5)] public string? SchemaVersion { get; set; }
    [Id(6)] public string MessageId { get; set; } = string.Empty;
    [Id(7)] public string? CorrelationId { get; set; }
    [Id(8)] public Dictionary<string, string> Headers { get; set; } = [];
}
```

### NotificationJson Helpers

The `NotificationJson` static class provides type-safe serialization:

```csharp
record WeatherAlert(string City, string Severity, int TemperatureC);

var envelope = NotificationJson.CreateEnvelope(
    "weather.alert",
    new WeatherAlert("Seattle", "critical", 6),
    schema: "weather.alert",
    schemaVersion: "2.0");

await publisher.NotifyAsync(envelope);

// Deserialize on the subscriber side
var notifications = await subscriber.QueryNotificationsAsync();
var alert = notifications[0].ReadPayload<WeatherAlert>();
```

### ReceiveNotificationAsync

Pushes a notification directly into an agent, bypassing pub/sub subscriptions. Useful in stream-processing pipelines:

```csharp
var stream = streamProvider.GetStream<string>(
    StreamId.Create("agent-event-processing", streamId));

await stream.SubscribeAsync(async (message, _) =>
{
    await processor.ReceiveNotificationAsync(new NotificationEnvelope
    {
        Topic = "weather.alert",
        Payload = message
    });
});
```

## Streams

The `PublishStreamAsync` method provides direct access to Orleans streams for real-time messaging beyond the notification system.

### Publishing to a Stream

```csharp
var agent = grainFactory.GetGrain<IAgentV2>("stream-agent");
var streamId = Guid.NewGuid();

await agent.PublishStreamAsync("agent-tests", streamId, "hello-stream");
```

### Subscribing from a Client

```csharp
var streamProvider = clusterClient.GetStreamProvider("agents");
var stream = streamProvider.GetStream<string>(
    StreamId.Create("agent-tests", streamId));

var handle = await stream.SubscribeAsync((payload, _) =>
{
    Console.WriteLine($"Received: {payload}");
    return Task.CompletedTask;
});

await handle.UnsubscribeAsync();
```

### Subscribing to Behavior Streams

Every behavior publishes to agent-specific streams. These are useful for building real-time UIs or logging pipelines:

```csharp
// Notification deliveries
var stream = streamProvider.GetStream<NotificationRecord>(
    StreamId.Create("agent-notifications", "subscriber-id"));

// Message history
var stream = streamProvider.GetStream<AgentMessage>(
    StreamId.Create("agent-history", "agent-id"));

// Events
var stream = streamProvider.GetStream<AgentEvent>(
    StreamId.Create("agent-events", "agent-id"));
```

## Summary of Stream Namespaces

| Namespace | Payload Type | Emitted By |
|---|---|---|
| `agent-events` | `AgentEvent` | `AppendEventAsync` |
| `agent-history` | `AgentMessage` | `AppendMessageAsync`, `RespondAsync` |
| `agent-notifications` | `NotificationRecord` | `NotifyAsync` / `ReceiveNotificationAsync` |
| Custom (e.g. `agent-tests`) | `string` | `PublishStreamAsync` |
