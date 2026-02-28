# API Reference

Complete reference for all IAW interfaces, behavior contracts, and data types.

## IAgent

The root grain interface. Every agent exposes this single interface, which is a composition of eight behavior interfaces:

```csharp
public interface IAgent :
    IGrainWithStringKey,
    IAgentMetadataBehavior,
    IAgentStateBehavior,
    IAgentHistoryBehavior,
    IAgentEventsBehavior,
    IAgentNotificationsBehavior,
    IAgentTrackingBehavior,
    IAgentToolsBehavior,
    IAgentStreamsBehavior;
```

## Behavior Interfaces

### IAgentMetadataBehavior

```csharp
public interface IAgentMetadataBehavior
{
    Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default);
}
```

### IAgentStateBehavior

```csharp
public interface IAgentStateBehavior
{
    Task SetStateAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetStateValueAsync(string key, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default);
    Task<int> IncrementAsync(string counterKey, CancellationToken ct = default);
}
```

### IAgentHistoryBehavior

```csharp
public interface IAgentHistoryBehavior
{
    Task AddHistoryAsync(string role, string content, CancellationToken ct = default);
    Task<List<AgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default);
}
```

### IAgentEventsBehavior

```csharp
public interface IAgentEventsBehavior
{
    Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default);
    Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default);
}
```

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

### IAgentTrackingBehavior

```csharp
public interface IAgentTrackingBehavior
{
    Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default);
    Task StopTrackingAsync(CancellationToken ct = default);
    Task<AgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default);
}
```

### IAgentToolsBehavior

```csharp
public interface IAgentToolsBehavior
{
    Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);
}
```

### IAgentStreamsBehavior

```csharp
public interface IAgentStreamsBehavior
{
    Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);
}
```

## Contract Types

### AgentMetadata

Returned by `GetMetadataAsync`. Identifies the agent and lists its capabilities.

```csharp
[GenerateSerializer]
public sealed class AgentMetadata
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string DisplayName { get; set; } = string.Empty;
    [Id(2)] public List<string> Capabilities { get; set; } = [];
}
```

### AgentHistoryEntry

A single conversation turn stored by `AddHistoryAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentHistoryEntry
{
    [Id(0)] public string Role { get; set; } = string.Empty;
    [Id(1)] public string Content { get; set; } = string.Empty;
    [Id(2)] public DateTimeOffset TimestampUtc { get; set; }
}
```

### AgentEventRecord

An immutable event log entry created by `PublishEventAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentEventRecord
{
    [Id(0)] public string Name { get; set; } = string.Empty;
    [Id(1)] public string? Payload { get; set; }
    [Id(2)] public DateTimeOffset TimestampUtc { get; set; }
}
```

### NotificationEnvelope

A rich notification message with metadata, used as input to `NotifyAsync` and `ReceiveNotificationAsync`.

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

### AgentTrackingStatus

Status of the periodic tracking timer, returned by `GetTrackingStatusAsync`.

```csharp
[GenerateSerializer]
public sealed class AgentTrackingStatus
{
    [Id(0)] public bool IsTracking { get; set; }
    [Id(1)] public int TickCount { get; set; }
    [Id(2)] public DateTimeOffset? StartedAtUtc { get; set; }
    [Id(3)] public TimeSpan Interval { get; set; }
    [Id(4)] public int MaxTicks { get; set; }
}
```

## NotificationJson

Static helper class for type-safe notification serialization and deserialization.

### CreateEnvelope

Creates a `NotificationEnvelope` with a serialized typed payload.

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

- Throws `ArgumentException` if `topic` is null or whitespace
- Sets `ContentType` to `"application/json"`
- Generates a new `MessageId` if none is provided
- Copies headers into a case-insensitive dictionary

### ReadPayload (NotificationEnvelope)

Extension method to deserialize the payload from a `NotificationEnvelope`.

```csharp
public static TPayload? ReadPayload<TPayload>(
    this NotificationEnvelope notification,
    JsonSerializerOptions? serializerOptions = null)
```

- Throws `ArgumentNullException` if `notification` is null
- Throws `InvalidOperationException` if `ContentType` does not contain `"json"`
- Returns `default` if `Payload` is null or whitespace

### ReadPayload (NotificationRecord)

Extension method to deserialize the payload from a `NotificationRecord`.

```csharp
public static TPayload? ReadPayload<TPayload>(
    this NotificationRecord notification,
    JsonSerializerOptions? serializerOptions = null)
```

- Same validation behavior as the `NotificationEnvelope` overload

## ITelegramBot

Extends `IAgent` with Telegram-specific messaging. See the [Telegram Bot guide](/guide/telegram) for full usage details.

```csharp
public interface ITelegramBot : IAgent
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

### TelegramBotUpdate

```csharp
[GenerateSerializer]
public sealed class TelegramBotUpdate
{
    [Id(0)] public long ChatId { get; set; }
    [Id(1)] public int MessageId { get; set; }
    [Id(2)] public int? ThreadId { get; set; }
    [Id(3)] public string? Text { get; set; }
    [Id(4)] public string? CallbackQueryId { get; set; }
    [Id(5)] public string? CallbackData { get; set; }
    [Id(6)] public string? Username { get; set; }
    [Id(7)] public string? FirstName { get; set; }
    [Id(8)] public long? FromUserId { get; set; }
}
```

### TelegramSendResult

```csharp
[GenerateSerializer]
public sealed class TelegramSendResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public int MessageId { get; set; }
    [Id(2)] public string? Error { get; set; }

    public static TelegramSendResult Ok(int messageId);
    public static TelegramSendResult Fail(string error);
}
```

### TelegramInlineButton

```csharp
[GenerateSerializer]
public sealed class TelegramInlineButton
{
    [Id(0)] public string Text { get; set; } = string.Empty;
    [Id(1)] public string CallbackData { get; set; } = string.Empty;
}
```

### TelegramTopicRegistry

```csharp
[GenerateSerializer]
public sealed class TelegramTopicRegistry
{
    [Id(0)] public int AssistantThreadId { get; set; }
    [Id(1)] public int NotificationsThreadId { get; set; }
    [Id(2)] public int SettingsThreadId { get; set; }
    [Id(3)] public Dictionary<string, int> TaskTopics { get; set; } = [];
}
```
