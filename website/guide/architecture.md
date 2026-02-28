# Architecture

This page covers the agent class hierarchy, durable state model, behavior interfaces, LLM integration, and observability infrastructure.

## Class Hierarchy

```
DurableGrain (Orleans.Journaling)
  └── Agent (Core)
        ├── implements IAgent
        └── implements IRemindable
```

`Agent` is a primary-constructor class that extends `DurableGrain` from `Microsoft.Orleans.Journaling`. It implements two interfaces: `IAgent` (the grain contract) and `IRemindable` (for tracking reminders).

`DurableGrain` provides journaled, transactional state persistence. All state mutations are committed via `WriteStateAsync()`.

## Durable State Collections

The `Agent` constructor accepts six durable state collections, each annotated with `[Memory("name")]`:

| Parameter | Type | Storage Key | Purpose |
|---|---|---|---|
| `values` | `IDurableDictionary<string, string>` | `agent-values` | Arbitrary key-value state |
| `history` | `IDurableList<AgentHistoryEntry>` | `agent-history` | Conversation history (role, content, timestamp) |
| `events` | `IDurableList<AgentEventRecord>` | `agent-events` | Named events with optional payload |
| `subscriptions` | `IDurableDictionary<string, List<string>>` | `agent-subscriptions` | Topic-to-subscriber mappings |
| `notifications` | `IDurableList<NotificationRecord>` | `agent-notifications` | Received notification records |
| `tracking` | `IDurableDictionary<string, AgentTrackingStatus>` | `agent-tracking` | Periodic tracking state |

All collections are backed by Orleans journaled grain storage, meaning they survive grain deactivation and silo restarts.

## The IAgent Interface

`IAgent` composes eight behavior interfaces plus `IGrainWithStringKey`:

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
Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default);
```

Returns the agent's `Id`, `DisplayName`, and `Capabilities` list. Default capabilities are: `state`, `history`, `events`, `notifications`, `tracking`, `streams`, `tools`.

### IAgentStateBehavior

```csharp
Task SetStateAsync(string key, string value, CancellationToken ct = default);
Task<string?> GetStateValueAsync(string key, CancellationToken ct = default);
Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default);
Task<int> IncrementAsync(string counterKey, CancellationToken ct = default);
```

Key-value state stored in `IDurableDictionary<string, string>`. `IncrementAsync` parses the current value as an integer, increments it, and persists the result.

### IAgentHistoryBehavior

```csharp
Task AddHistoryAsync(string role, string content, CancellationToken ct = default);
Task<List<AgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default);
```

Appends entries to the durable history list and publishes each entry to the `"agent-history"` Orleans stream.

### IAgentEventsBehavior

```csharp
Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default);
Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default);
```

Records named events (with optional JSON payload) to durable storage and publishes to the `"agent-events"` stream.

### IAgentNotificationsBehavior

```csharp
Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default);
Task NotifyAsync(string topic, string payload, CancellationToken ct = default);
Task NotifyAsync(NotificationEnvelope notification, CancellationToken ct = default);
Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default);
Task ReceiveNotificationAsync(NotificationEnvelope notification, CancellationToken ct = default);
Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default);
```

Pub/sub notification system. `SubscribeAsync` registers a subscriber agent for a topic. `NotifyAsync` fans out the notification to all subscribers by calling `ReceiveNotificationAsync` on each subscriber grain.

`NotificationEnvelope` carries structured metadata: `Topic`, `Payload`, `ContentType`, `Schema`, `SchemaVersion`, `MessageId`, `CorrelationId`, `Headers`, and `TimestampUtc`.

### IAgentTrackingBehavior

```csharp
Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default);
Task StopTrackingAsync(CancellationToken ct = default);
Task<AgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default);
```

Schedules periodic ticks. If the interval is >= 1 minute, Orleans reminders are used (surviving silo restarts). For shorter intervals, grain timers are used. Tracking stops automatically after `maxTicks`.

### IAgentToolsBehavior

```csharp
Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);
```

Invokes a tool by name from the list returned by `DefineTools()`. Looks up the matching `AIFunction`, calls it with the provided arguments, and records the call via `AgentObservability`.

### IAgentStreamsBehavior

```csharp
Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);
```

Publishes a string message to an Orleans stream using the `"agents"` stream provider.

## LLM Integration

The `Agent` class integrates with LLMs through `Microsoft.Extensions.AI`:

```csharp
protected AIAgent? Llm { get; private set; }

public virtual void Activate(IChatClient chatClient)
{
    var tools = DefineTools();
    Llm = chatClient.AsAIAgent(SystemPrompt, Id, DisplayName, [.. tools], null, null);
}
```

Call `Activate(IChatClient)` to initialize the LLM. The `IChatClient` is converted to an `AIAgent` (from `Microsoft.Agents.AI`) with the agent's system prompt, identity, and tools.

The `SendAsync` method streams LLM responses:

```csharp
public virtual async IAsyncEnumerable<string> SendAsync(
    string message,
    CancellationToken ct = default)
```

It records the user message to history, streams tokens from the LLM via `Llm.RunStreamingAsync()`, records the complete assistant response to history, and emits OpenTelemetry traces and metrics throughout.

## Observability

The `AgentObservability` class provides built-in telemetry:

- **ActivitySource**: `"Core.Agent"` -- emits distributed traces for `agent.send` operations
- **Meter**: `"Core.Agent"` -- exposes three counters:
  - `core.agent.sends` -- total LLM send operations
  - `core.agent.tool_calls` -- total tool invocations
  - `core.agent.failures` -- total failures during sends

Activities include `agent.id` and `agent.display_name` tags for filtering in your observability backend. All telemetry follows OpenTelemetry conventions and integrates with the .NET Aspire dashboard.
