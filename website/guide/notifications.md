# Notifications & Events

IAW agents communicate through three complementary mechanisms: **events** (audit log), **notifications** (pub/sub delivery), and **streams** (real-time Orleans streams). All three are part of the `IAgent` interface, composed from dedicated behavior interfaces.

## Events

Events are an immutable audit log stored on each agent grain. Publishing an event appends an `AgentEventRecord` to durable storage and broadcasts it on the `agent-events` Orleans stream.

### IAgentEventsBehavior

```csharp
public interface IAgentEventsBehavior
{
    Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default);
    Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default);
}
```

### Publishing Events

```csharp
var agent = grainFactory.GetGrain<IAgent>("weather-agent");

await agent.PublishEventAsync("weather.refresh", "Seattle");
await agent.PublishEventAsync("weather.alert", "rain");
```

### Reading the Event Log

```csharp
var events = await agent.GetEventsAsync();

foreach (var e in events)
{
    Console.WriteLine($"{e.TimestampUtc}: {e.Name} - {e.Payload}");
}
```

### Subscribing to the Event Stream

Every `PublishEventAsync` call also emits the `AgentEventRecord` on an Orleans stream. Clients can subscribe to receive events in real time:

```csharp
var streamProvider = clusterClient.GetStreamProvider("agents");
var stream = streamProvider.GetStream<AgentEventRecord>(
    StreamId.Create("agent-events", "weather-agent"));

var handle = await stream.SubscribeAsync((entry, _) =>
{
    Console.WriteLine($"Event: {entry.Name} = {entry.Payload}");
    return Task.CompletedTask;
});
```

### AgentEventRecord

```csharp
public sealed class AgentEventRecord
{
    public string Name { get; set; }
    public string? Payload { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}
```

## Notifications

Notifications provide pub/sub message delivery between agents. An agent subscribes other agents to topics, then publishes messages that are delivered to all subscribers. Each delivery appends a `NotificationRecord` to the subscriber and emits on the `agent-notifications` stream.

### IAgentNotificationsBehavior

```csharp
public interface IAgentNotificationsBehavior
{
    Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default);
    Task NotifyAsync(string topic, string payload, CancellationToken ct = default);
    Task NotifyAsync(NotificationEnvelope notification, CancellationToken ct = default);
    Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default);
    Task ReceiveNotificationAsync(NotificationEnvelope notification, CancellationToken ct = default);
    Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default);
}
```

### Basic Pub/Sub

```csharp
var publisher = grainFactory.GetGrain<IAgent>("publisher");
var subscriber = grainFactory.GetGrain<IAgent>("subscriber");

// Register subscriber for a topic on the publisher
await publisher.SubscribeAsync("weather.alert", "subscriber");

// Publish a plain-text notification
await publisher.NotifyAsync("weather.alert", "storm");

// Subscriber receives it automatically; read stored notifications
var notifications = await subscriber.GetNotificationsAsync();
// notifications[0].Topic == "weather.alert"
// notifications[0].Payload == "storm"
```

### Typed Notifications with NotificationEnvelope

For richer metadata, use the `NotificationEnvelope` overload which carries content type, schema information, correlation IDs, and custom headers:

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
public sealed class NotificationEnvelope
{
    public string Topic { get; set; }
    public string Payload { get; set; }
    public string ContentType { get; set; } = "application/json";
    public string? Schema { get; set; }
    public string? SchemaVersion { get; set; }
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Headers { get; set; } = [];
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### NotificationRecord

When a notification is delivered to a subscriber, it is stored as a `NotificationRecord`:

```csharp
public sealed class NotificationRecord
{
    public string Topic { get; set; }
    public string Payload { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string ContentType { get; set; } = "application/json";
    public string? Schema { get; set; }
    public string? SchemaVersion { get; set; }
    public string MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public Dictionary<string, string> Headers { get; set; } = [];
}
```

## NotificationJson Helpers

The `NotificationJson` static class provides type-safe serialization for notification payloads.

### CreateEnvelope

Serializes a typed payload into a `NotificationEnvelope`:

```csharp
public static NotificationEnvelope CreateEnvelope<TPayload>(
    string topic,
    TPayload payload,
    string? schema = null,
    string? schemaVersion = null,
    string? messageId = null,
    string? correlationId = null,
    IReadOnlyDictionary<string, string>? headers = null,
    JsonSerializerOptions? serializerOptions = null)
```

Usage:

```csharp
record WeatherAlert(string City, string Severity, int TemperatureC);

var envelope = NotificationJson.CreateEnvelope(
    "weather.alert",
    new WeatherAlert("Seattle", "critical", 6),
    schema: "weather.alert",
    schemaVersion: "2.0",
    correlationId: Guid.NewGuid().ToString("N"),
    headers: new Dictionary<string, string>
    {
        ["source"] = "weather-service"
    });

await publisher.NotifyAsync(envelope);
```

### ReadPayload

Extension methods to deserialize the JSON payload from a `NotificationEnvelope` or `NotificationRecord`:

```csharp
public static TPayload? ReadPayload<TPayload>(
    this NotificationEnvelope notification,
    JsonSerializerOptions? serializerOptions = null)

public static TPayload? ReadPayload<TPayload>(
    this NotificationRecord notification,
    JsonSerializerOptions? serializerOptions = null)
```

Usage:

```csharp
var notifications = await subscriber.GetNotificationsAsync();
var entry = notifications[0];

var alert = entry.ReadPayload<WeatherAlert>();
// alert.City == "Seattle"
// alert.Severity == "critical"
// alert.TemperatureC == 6
```

Both methods validate that the `ContentType` contains `"json"` and throw `InvalidOperationException` if it does not.

### ReceiveNotificationAsync

The `ReceiveNotificationAsync` method directly pushes a notification into an agent, bypassing the pub/sub subscription mechanism. This is useful in stream-processing pipelines where you subscribe to an Orleans stream and forward messages to an agent:

```csharp
var stream = streamProvider.GetStream<string>(
    StreamId.Create("agent-event-processing", streamId));

await stream.SubscribeAsync(async (message, _) =>
{
    await processor.ReceiveNotificationAsync("weather.alert", message);
    await processor.IncrementAsync("processed-count");
    await processor.PublishEventAsync("processing.completed", message);
});
```

## Streams

The `IAgentStreamsBehavior` provides direct access to Orleans streams for real-time messaging beyond the notification system.

### IAgentStreamsBehavior

```csharp
public interface IAgentStreamsBehavior
{
    Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);
}
```

### Publishing to a Stream

```csharp
var agent = grainFactory.GetGrain<IAgent>("stream-agent");
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

// Clean up when done
await handle.UnsubscribeAsync();
```

### Subscribing to the Notification Stream

Every notification delivery also emits on an agent-specific notification stream. This is useful for building real-time UIs or logging pipelines:

```csharp
var stream = streamProvider.GetStream<NotificationRecord>(
    StreamId.Create("agent-notifications", "subscriber-id"));

await stream.SubscribeAsync((entry, _) =>
{
    Console.WriteLine($"Notification: {entry.Topic} = {entry.Payload}");
    return Task.CompletedTask;
});
```

Similarly, history additions emit on `agent-history`:

```csharp
var stream = streamProvider.GetStream<AgentHistoryEntry>(
    StreamId.Create("agent-history", "agent-id"));

await stream.SubscribeAsync((entry, _) =>
{
    Console.WriteLine($"{entry.Role}: {entry.Content}");
    return Task.CompletedTask;
});
```

## Summary of Stream Namespaces

| Namespace | Payload Type | Emitted By |
|---|---|---|
| `agent-events` | `AgentEventRecord` | `PublishEventAsync` |
| `agent-history` | `AgentHistoryEntry` | `AddHistoryAsync` |
| `agent-notifications` | `NotificationRecord` | `NotifyAsync` / `ReceiveNotificationAsync` |
| Custom (e.g. `agent-tests`) | `string` | `PublishStreamAsync` |
