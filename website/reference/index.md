# API Reference

Complete reference for all IAW V2 interfaces, contracts, and data types.

## IAgentV2

The root grain interface. Every V2 agent exposes this single interface:

```csharp
public interface IAgentV2 : IGrainWithStringKey
{
    Task<AgentProfile> GetProfileAsync(CancellationToken ct = default);

    Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct = default);

    Task AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<List<AgentMessage>> QueryMessagesAsync(AgentMessageQuery? query = null, CancellationToken ct = default);

    Task SetMemoryAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetMemoryAsync(string key, CancellationToken ct = default);

    Task AppendEventAsync(AgentEvent agentEvent, CancellationToken ct = default);
    Task<List<AgentEvent>> QueryEventsAsync(AgentEventQuery? query = null, CancellationToken ct = default);

    Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default);
    Task NotifyAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task ReceiveNotificationAsync(NotificationEnvelope envelope, CancellationToken ct = default);
    Task<List<NotificationRecord>> QueryNotificationsAsync(CancellationToken ct = default);

    Task StartScheduleAsync(TimeSpan interval, int? maxTicks = null, CancellationToken ct = default);
    Task StopScheduleAsync(CancellationToken ct = default);
    Task<ScheduleStatus> GetScheduleStatusAsync(CancellationToken ct = default);

    Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);

    Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);
}
```

## AgentV2 Override Points

These are the methods you override when building agents:

```csharp
public abstract class AgentV2 : DurableGrain, IAgentV2, IRemindable
{
    // Required: agent identity and configuration
    protected abstract AgentProfile Profile { get; }

    // Optional: handle incoming requests (default returns "Not implemented")
    protected virtual Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default);

    // Optional: expose tools for LLM and InvokeToolAsync (default returns empty list)
    protected virtual IReadOnlyList<AITool> DefineTools();

    // Optional: handle scheduled ticks (default is no-op)
    protected virtual Task OnScheduleTickAsync(int tickCount, CancellationToken ct = default);

    // Optional: customize how subscriber grains are resolved
    protected virtual IAgentV2 ResolveSubscriber(string subscriberId);
}
```

### Protected Properties

```csharp
// Read access to the durable message list
protected IDurableList<AgentMessage> Messages { get; }

// Read access to the durable key-value memory
protected IDurableDictionary<string, string> Memory { get; }

// Read access to the durable event list
protected IDurableList<AgentEvent> Events { get; }

// The grain's primary key string
protected string AgentId { get; }
```

### Helper Methods

```csharp
// Call an LLM with the full message history and tools
protected Task<AgentReply> RespondWithLlmAsync(
    IChatClient chatClient, AgentRequest request, CancellationToken ct = default);
```

## Contract Types

### AgentProfile

Returned by `GetProfileAsync`. Identifies the agent and its configuration.

```csharp
[GenerateSerializer]
public sealed class AgentProfile
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string DisplayName { get; set; } = string.Empty;
    [Id(2)] public string? Description { get; set; }
    [Id(3)] public string Instructions { get; set; } = string.Empty;
    [Id(4)] public List<string> Capabilities { get; set; } = [];
}
```

### AgentRequest

Input to `RespondAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentRequest
{
    [Id(0)] public string Input { get; set; } = string.Empty;
    [Id(1)] public string? ConversationId { get; set; }
    [Id(2)] public Dictionary<string, string> Metadata { get; set; } = [];
    [Id(3)] public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### AgentReply

Output from `RespondAsync` and `OnRespondAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentReply
{
    [Id(0)] public string Output { get; set; } = string.Empty;
    [Id(1)] public string? ModelId { get; set; }
    [Id(2)] public Dictionary<string, string> Metadata { get; set; } = [];
    [Id(3)] public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
```

### AgentMessage

A single conversation turn stored by `AppendMessageAsync` and `RespondAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentMessage
{
    [Id(0)] public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    [Id(1)] public string Role { get; set; } = string.Empty;
    [Id(2)] public string Content { get; set; } = string.Empty;
    [Id(3)] public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    [Id(4)] public Dictionary<string, string> Metadata { get; set; } = [];
}
```

### AgentMessageQuery

Filter for `QueryMessagesAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentMessageQuery
{
    [Id(0)] public int? Limit { get; set; }
    [Id(1)] public DateTimeOffset? SinceUtc { get; set; }
    [Id(2)] public string? Role { get; set; }
    [Id(3)] public bool Descending { get; set; }
}
```

### AgentEvent

An event log entry created by `AppendEventAsync`.

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

### AgentEventQuery

Filter for `QueryEventsAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentEventQuery
{
    [Id(0)] public int? Limit { get; set; }
    [Id(1)] public DateTimeOffset? SinceUtc { get; set; }
    [Id(2)] public string? Type { get; set; }
    [Id(3)] public bool Descending { get; set; }
}
```

### ScheduleStatus

Returned by `GetScheduleStatusAsync`.

```csharp
[GenerateSerializer]
public sealed class ScheduleStatus
{
    [Id(0)] public bool IsRunning { get; set; }
    [Id(1)] public TimeSpan Interval { get; set; }
    [Id(2)] public int TickCount { get; set; }
    [Id(3)] public int? MaxTicks { get; set; }
}
```

### NotificationEnvelope

A rich notification message used as input to `NotifyAsync` and `ReceiveNotificationAsync`.

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

A delivered notification stored on the subscriber agent.

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

## NotificationJson

Static helper class for type-safe notification serialization and deserialization.

### CreateEnvelope

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

Both methods validate that `ContentType` contains `"json"` and throw `InvalidOperationException` if it does not.

## ITelegramConversation

Extends `IAgent` with Telegram-specific messaging. See the [Telegram Bot guide](/guide/telegram) for full usage details.

```csharp
public interface ITelegramConversation : IAgent
{
    [OneWay]
    Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct = default);
    Task<TelegramSendResult> SendText(long chatId, string text, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> SendMarkdown(long chatId, string markdown, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> SendKeyboard(long chatId, string text, TelegramInlineButton[][] buttons, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> EditMessage(long chatId, int messageId, string text, TelegramInlineButton[][]? buttons = null, CancellationToken ct = default);
    Task SendTyping(long chatId, int? threadId = null, CancellationToken ct = default);
    Task SetReaction(long chatId, int messageId, string emoji, CancellationToken ct = default);
    Task PinMessage(long chatId, int messageId, int? threadId = null, CancellationToken ct = default);
    Task<int> CreateTopic(long chatId, string name, CancellationToken ct = default);
    Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct = default);
    Task SetWebhook(string url, string? secretToken = null, CancellationToken ct = default);
    Task AnswerCallback(string callbackQueryId, string? text = null, CancellationToken ct = default);
}
```
