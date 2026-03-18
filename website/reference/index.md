# API Reference

Complete reference for all IAW interfaces, contracts, and data types.

## Core

### IAgent

The root grain interface. Every agent exposes this interface:

```csharp
public interface IAgent : IGrainWithStringKey
{
    // Conversation
    IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken ct);
    Task<string> GetResponse(string prompt, CancellationToken ct);
    Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken ct);
    Task ClearHistoryAsync(CancellationToken ct);

    // State
    Task<AgentState> GetStateAsync(CancellationToken ct);
    Task SetWorkspaceAsync(string path, CancellationToken ct);

    // Metadata
    Task<AgentMetadata> GetMetadataAsync(CancellationToken ct);
    Task<AgentCapabilities> GetCapabilitiesAsync(CancellationToken ct);

    // Events
    Task HandleEventAsync(AgentEvent agentEvent, CancellationToken ct);
    Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct);

    // Streams
    Task PublishToStreamAsync(AgentEvent evt, CancellationToken ct);
    Task<IReadOnlyList<string>> GetActiveSubscriptionsAsync(CancellationToken ct);

    // Lifecycle
    Task CancelAsync(CancellationToken ct);
}
```

### Agent (base class)

The abstract base class for all agents:

```csharp
public abstract partial class Agent(
    AgentDurableState durableState,
    IChatClient chatClient)
    : DurableGrain, IAgent
```

#### Virtual Members

| Member | Type | Default |
|---|---|---|
| `Instructions` | `string` | `"You are a helpful AI assistant..."` |
| `DisplayName` | `string` | `GetType().Name` |
| `DefineTools()` | `IReadOnlyList<AITool>` | Empty list |
| `OnTrackingDueAsync(item, ct)` | `Task` | LLM-powered check with change detection |
| `HandleEventAsync(evt, ct)` | `Task` | No-op |
| `AgentKindValue` | `AgentKind` | `AgentKind.Static` |

#### Protected Members

| Member | Type | Purpose |
|---|---|---|
| `ChatClient` | `IChatClient` | The injected LLM client |
| `History` | `IDurableList<ChatMessage>` | Conversation history |
| `State` | `IDurableDictionary<string, StateEntry>` | Key-value state |
| `EventLog` | `IDurableList<AgentEvent>` | Event audit log |
| `TrackingItems` | `IDurableDictionary<string, TrackingItem>` | Tracking items |
| `StreamProvider` | `IStreamProvider` | Orleans stream provider (`"agents"`) |
| `AgentCancellation` | `CancellationToken` | Agent-scoped cancellation token |

#### Protected Methods

| Method | Purpose |
|---|---|
| `PublishAsync(name, payload, ct)` | Publish an untyped event |
| `PublishTypedAsync<TEvent>(evt, ct)` | Publish a typed `IEvent` |
| `GetWorkspacePath()` | Get current workspace path (or null) |
| `WriteStateAsync(ct)` | Persist all state changes |
| `BuildSafeErrorMessage(ex)` | Format exception for safe display |

## Core.Communication.Messages

### IAgentMessage

```csharp
public interface IAgentMessage
{
    string SourceAgentId { get; }
    string CorrelationId { get; }
    DateTimeOffset Timestamp { get; }
}
```

### ICommand

```csharp
public interface ICommand : IAgentMessage;
```

### IEvent

```csharp
public interface IEvent : IAgentMessage;
```

### INotification

```csharp
public interface INotification : IAgentMessage;
```

### Built-in Commands

| Type | Fields |
|---|---|
| `AssignTaskCommand` | `Description`, `WorkspacePath?` |

### Built-in Events

| Type | Fields |
|---|---|
| `CodeChangedEvent` | `FilePaths`, `CommitSha?` |
| `BuildCompletedEvent` | `Success`, `CommitSha?`, `Output?` |
| `TestResultEvent` | `Passed`, `TotalTests`, `FailedTests`, `Summary?` |
| `DeployCompletedEvent` | `Success`, `Environment`, `Version?` |
| `HealthCheckEvent` | `ServiceName`, `Healthy`, `ResponseTimeMs?` |
| `AgentActivatedEvent` | `AgentType` |
| `StateChangedEvent` | `Key`, `OldValue`, `NewValue` |

### Built-in Notifications

| Type | Fields |
|---|---|
| `AlertNotification` | `Severity`, `Message` |
| `ProgressNotification` | `Step`, `Status`, `Progress?` |
| `ReviewRequestNotification` | `FilePath`, `Description` |

## Core.Communication

### IStreamConsumer&lt;TEvent&gt;

```csharp
public interface IStreamConsumer<TEvent> where TEvent : IEvent
{
    Task OnStreamEventAsync(TEvent evt, StreamSequenceToken? token);
}
```

### IStreamProducer&lt;TEvent&gt;

```csharp
public interface IStreamProducer<TEvent> where TEvent : IEvent
{
    Task PublishToStreamAsync(TEvent evt, CancellationToken ct = default);
}
```

### IBroadcaster&lt;TMessage&gt;

```csharp
public interface IBroadcaster<TMessage> where TMessage : IAgentMessage
{
    Task<BroadcastResult> BroadcastAsync(TMessage message, CancellationToken ct = default);
    Task RegisterReceiverAsync(string receiverId);
    Task UnregisterReceiverAsync(string receiverId);
    Task<IReadOnlyList<string>> GetReceiversAsync();
}
```

### IReceiver&lt;TMessage&gt;

```csharp
public interface IReceiver<TMessage> where TMessage : IAgentMessage
{
    Task<MessageReceipt> ReceiveAsync(TMessage message, CancellationToken ct = default);
    Task<bool> CanReceiveAsync(CancellationToken ct = default);
}
```

### INotifier&lt;TNotification&gt;

```csharp
public interface INotifier<TNotification> where TNotification : INotification
{
    Task NotifyAsync(TNotification notification, CancellationToken ct = default);
    Task SubscribeObserverAsync(IAgentObserver<TNotification> observer);
    Task UnsubscribeObserverAsync(IAgentObserver<TNotification> observer);
}
```

### IAgentObserver&lt;TEvent&gt;

```csharp
public interface IAgentObserver<TEvent> : IGrainObserver where TEvent : INotification
{
    void OnEvent(TEvent evt);
    void OnError(Exception ex);
}
```

### BroadcastResult

```csharp
[GenerateSerializer]
public record BroadcastResult(
    [property: Id(0)] int TotalReceivers,
    [property: Id(1)] int Delivered,
    [property: Id(2)] int Failed,
    [property: Id(3)] string[] FailedReceiverIds);
```

### MessageReceipt

```csharp
[GenerateSerializer]
public record MessageReceipt(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] string ReceiptId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string? RejectionReason);
```

## Core Data Types

### AgentEvent

```csharp
[GenerateSerializer]
public record AgentEvent(
    [property: Id(0)] string EventName,
    [property: Id(1)] string SourceAgentId,
    [property: Id(2)] string CorrelationId,
    [property: Id(3)] DateTimeOffset Timestamp,
    [property: Id(4)] Dictionary<string, object> Payload);
```

### AgentMetadata

```csharp
[GenerateSerializer]
public record AgentMetadata(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] AgentKind Kind,
    [property: Id(4)] string[] Capabilities,
    [property: Id(5)] string[] Publishes,
    [property: Id(6)] string[] Subscribes);
```

### AgentCapabilities

```csharp
[GenerateSerializer]
public record AgentCapabilities(
    [property: Id(0)] bool HasMemory,
    [property: Id(1)] bool HasP2P,
    [property: Id(2)] bool HasEvents,
    [property: Id(3)] bool HasTimers,
    [property: Id(4)] bool IsCancellable,
    [property: Id(5)] bool IsMultiState,
    [property: Id(6)] bool HasTools,
    [property: Id(7)] bool IsSecure);
```

### AgentState

```csharp
[GenerateSerializer]
public record AgentState(
    [property: Id(0)] Dictionary<string, StateEntry> Entries);
```

### StateEntry

```csharp
[GenerateSerializer]
public record StateEntry(
    [property: Id(0)] string Key,
    [property: Id(1)] object Value);
```

### ChatMessage

```csharp
[GenerateSerializer]
public sealed record ChatMessage
{
    [Id(0)] public string Role { get; init; } = string.Empty;
    [Id(1)] public string Content { get; init; } = string.Empty;
    [Id(2)] public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
```

### TrackingItem

```csharp
[GenerateSerializer]
public record TrackingItem(
    [property: Id(0)] string Id,
    [property: Id(1)] string Description,
    [property: Id(2)] TimeSpan Interval,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] DateTimeOffset? LastCheckAt,
    [property: Id(5)] string? LastResult);
```

### AgentKind

```csharp
[GenerateSerializer]
public enum AgentKind { Static, Dynamic }
```

### AgentResponse

```csharp
public enum AgentResponseKind { Text, ToolCall, ToolResult, Error, Final }

[GenerateSerializer]
public record AgentResponse(
    [property: Id(0)] AgentResponseKind Kind,
    [property: Id(1)] string Content,
    [property: Id(2)] string? ToolName = null,
    [property: Id(3)] Dictionary<string, object>? Metadata = null);
```

### AgentConfiguration

```csharp
[GenerateSerializer]
public record AgentConfiguration(
    [property: Id(0)] string? DisplayName,
    [property: Id(1)] string? SystemPrompt,
    [property: Id(2)] string[]? ToolNames,
    [property: Id(3)] string? WorkspacePath,
    [property: Id(4)] string[]? SubscribeToStreams);
```

## Core.Registry

### IAgentRegistryGrain

```csharp
public interface IAgentRegistryGrain : IGrainWithStringKey
{
    Task RegisterAsync(AgentRegistration registration);
    Task UnregisterAsync(string agentType);
    Task<IReadOnlyList<AgentRegistration>> GetAllAsync();
    Task<IReadOnlyList<AgentRegistration>> QueryAsync(AgentQuery query);
    Task<AgentRegistration?> GetByTypeAsync(string agentType);
}
```

### AgentRegistration

```csharp
[GenerateSerializer]
public record AgentRegistration(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] AgentKind Kind,
    [property: Id(4)] string[] Capabilities,
    [property: Id(5)] string[] Publishes,
    [property: Id(6)] string[] Subscribes);
```

### AgentQuery

```csharp
[GenerateSerializer]
public record AgentQuery(
    [property: Id(0)] AgentKind? Kind = null,
    [property: Id(1)] string[]? Capabilities = null,
    [property: Id(2)] string[]? Publishes = null,
    [property: Id(3)] string[]? Subscribes = null);
```

## Core.Attributes

### CapabilityAttribute

Declares a capability for the agent registry:

```csharp
[Capability("code-review")]
public class CodeReviewAgent : Agent { ... }
```

### PublishesAttribute

Declares an event this agent publishes:

```csharp
[Publishes("review.completed")]
public class CodeReviewAgent : Agent { ... }
```

### SubscribesAttribute

Declares an event this agent subscribes to:

```csharp
[Subscribes("code.changed")]
public class CodeReviewAgent : Agent { ... }
```

## Core.Tools

### FileTools

| Method | Params | Returns |
|---|---|---|
| `ReadFileAsync` | `path` | File contents or error |
| `WriteFileAsync` | `path`, `content` | Confirmation |
| `ListFiles` | `directory`, `pattern` | Matching file paths |
| `SearchCode` | `pattern`, `directory`, `fileFilter` | Matching lines |

### ShellTools

| Method | Params | Returns |
|---|---|---|
| `RunDotnetAsync` | `arguments`, `workingDirectory?` | Command output |
| `RunShellAsync` | `command`, `workingDirectory?` | Command output |

### WebTools

| Method | Params | Returns |
|---|---|---|
| `FetchUrlAsync` | `url` | Page content or error |

### WorkspaceTools

| Method | Params | Returns |
|---|---|---|
| `SetWorkspace` | `path` | Confirmation |
| `GetWorkspace` | -- | Current path |

## Core.Observability

### AgentTelemetry

| Metric | Type | Description |
|---|---|---|
| `agents.events.published` | Counter | Events published |
| `agents.events.handled` | Counter | Events handled |
| `agents.activations` | Counter | Agent activations |
| `agents.messages.sent` | Counter | Messages processed |
| `agents.conversations.errors` | Counter | Conversation errors |
| `agents.events.handle_duration` | Histogram | Event handling duration (seconds) |
| `agents.conversations.duration` | Histogram | Conversation turn duration (seconds) |

ActivitySource name: `"IAW"`
Meter name: `"IAW"`
