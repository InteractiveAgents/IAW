# V3 Agent Behaviors Migration — Design Document

## Context

IAW V3 (`InteractiveAgents/IAW/src/Core/V3/`) is the opensource version of the production IAW Core (`src/Core/`). The V3 Agent currently has a minimal implementation: chat via `Microsoft.Agents.AI`, durable state/events/history, and a tools stub. The production core has 9 partial classes with rich behavior composition.

This design covers migrating behaviors one-by-one into V3 with:
- Type-safe message contracts (replacing `Dictionary<string, object>` payloads)
- Compile-time behavior composition via interfaces
- Full test coverage per behavior
- Documentation and samples for each mechanism

Related docs:
- `2026-03-04-iaw-v2-complete-redesign.md`
- `2026-02-28-agent-unification-design.md`

## Decisions

| Decision | Choice |
|----------|--------|
| Migration order | Conversation+Tools+State → Events+Streams → Tracking → Observers |
| Behavior composition | Typed interfaces, compile-time — no string-based registration |
| DynamicAgent | Only place where runtime/string-based behavior configuration allowed |
| Message system | Typed hierarchy: IAgentMessage → ICommand / IEvent / INotification |
| Stream addressing | Type-derived stream names (PascalCase → dot.case), no magic strings |
| Namespace | `IAW.Core.V3` (will become `IAW.Core` at V3 graduation) |
| Testing | Each behavior gets dedicated test class + universal contract tests |
| Documentation | VitePress site updated per behavior with guides + API reference |

## 1. Current V3 State (Baseline)

### What Exists

```
V3/
├── Agent.cs                     — DurableGrain base, Microsoft.Agents.AI integration
├── Agent.Tools.cs               — DefineTools() stub, GetAllTools() empty
├── IAgent.cs                    — 4 methods: GetResponseStream, GetResponse, GetHistory, ClearHistoryAsync
├── ChatMessage.cs               — [GenerateSerializer] record (Role, Content, TimestampUtc)
├── AgentEvent.cs                — [GenerateSerializer] record (EventName, SourceAgentId, CorrelationId, Timestamp, Payload dict)
├── StateEntry.cs                — [GenerateSerializer] record StateDescriptor(Key, Value)
├── DurableChatHistoryProvider.cs — ChatHistoryProvider for Orleans journaling
├── WeatherAgent.cs              — Example agent with weather tools
└── Tools/WorkspaceTools.cs      — Workspace get/set tool
```

### Known Issues in Current V3

1. `WeatherAgent` constructor missing `eventLog` parameter (won't compile with base)
2. `AgentEvent.Payload` is `Dictionary<string, object>` — not type-safe
3. `StateEntry.cs` file contains `StateDescriptor` record — naming inconsistency
4. `Agent.Tools.cs` has conflicting `DefineTools()` return type (`IReadOnlyList<AITool>`) vs `Agent.cs` property `Tools` (`IList<AITool>`)
5. No telemetry, no lifecycle metadata, no diagnostics

## 2. Typed Message System

### Message Hierarchy

```csharp
// Marker interface — all agent messages must implement this
[GenerateSerializer]
public interface IAgentMessage
{
    string SourceAgentId { get; }
    string CorrelationId { get; }
    DateTimeOffset Timestamp { get; }
}

// "Do this" — directed, point-to-point, expects acknowledgment
public interface ICommand : IAgentMessage;

// "This happened" — broadcast via Orleans streams, informational
public interface IEvent : IAgentMessage;

// "You should know" — targeted, advisory, observer pattern
public interface INotification : IAgentMessage;
```

### Stream Name Resolution

Type name → stream name via convention:

```
CodeChangedEvent      → "code.changed"
BuildCompletedEvent   → "build.completed"
AlertNotification     → "alert"
TaskAssignedCommand   → "task.assigned"
```

The suffix (`Event`, `Command`, `Notification`) is stripped. PascalCase → dot.case.

### Built-in Message Types

```csharp
// Events
[GenerateSerializer]
public record AgentActivatedEvent(...) : IEvent;

[GenerateSerializer]
public record StateChangedEvent(string Key, object? OldValue, object? NewValue, ...) : IEvent;

// Commands
[GenerateSerializer]
public record AssignTaskCommand(string Description, string? WorkspacePath, ...) : ICommand;

// Notifications
[GenerateSerializer]
public record ProgressNotification(string Step, string Status, float? Progress, ...) : INotification;

[GenerateSerializer]
public record AlertNotification(string Severity, string Message, ...) : INotification;
```

## 3. Behavior Interfaces (Compile-Time Composition)

### Stream Composition Interfaces

```csharp
// Agent declares: "I publish T to streams"
public interface IStreamProducer<TEvent> where TEvent : IEvent
{
    Task PublishToStreamAsync(TEvent message);
}

// Agent declares: "I consume T from streams" — auto-subscribed on activation
public interface IStreamConsumer<TEvent> where TEvent : IEvent
{
    Task OnStreamEventAsync(TEvent message, StreamSequenceToken? token);
}

// Agent declares: "I broadcast T to registered receivers"
public interface IBroadcaster<TMessage> where TMessage : IAgentMessage
{
    Task<BroadcastResult> BroadcastAsync(TMessage message);
}

// Agent declares: "I receive T point-to-point"
public interface IReceiver<TMessage> where TMessage : IAgentMessage
{
    Task<MessageReceipt> ReceiveAsync(TMessage message);
}

// Agent declares: "I notify observers of T"
public interface INotifier<TNotification> where TNotification : INotification
{
    Task NotifyAsync(TNotification notification);
}
```

### Metadata Discovery

On activation, the Agent base class reflects on `this.GetType()` to discover:
- All `IStreamConsumer<T>` → auto-subscribe to streams
- All `IStreamProducer<T>` → register as publisher in metadata
- All `IBroadcaster<T>`, `INotifier<T>`, `IReceiver<T>` → populate capabilities
- `[Capability]`, `[Publishes]`, `[Subscribes]` attributes → additional metadata

### Type-Safety Boundary

| Agent Type | Behavior Registration | Rationale |
|---|---|---|
| Static agents | Compile-time interfaces + `DefineTools()` | Type-safe, testable, discoverable |
| DynamicAgent | Runtime via `ConfigureAsync(AgentConfiguration)` | User-created agents composing at runtime |

DynamicAgent is the **only** place where string-based stream subscription is allowed.

## 4. Behavior Migration — Phase A: Conversation + Tools + State

### Agent.Conversation.cs (partial)

Port from source with adaptations for Microsoft.Agents.AI:
- `GetResponseStream()` already works via `AIAgent.RunStreamingAsync`
- Add: context provider injection (`IAIContextProvider`)
- Add: token usage tracking via `StreamingUsageChatClient`
- Add: error handling with `AgentResponseKind.Error`

### Agent.Tools.cs (partial)

Complete the stub:
- Register core tools: `WorkspaceTools`, `FileTools`, `ShellTools`, `WebTools`
- `DefineTools()` for subclass-defined tools
- `GetAllTools()` merges core + subclass tools
- Tool metadata exposed via `GetToolDescriptions()`

### Agent.State.cs (partial)

- `SetWorkspaceAsync(string path)` → stores to state
- `GetStateAsync()` → returns all state entries
- `GetWorkspacePath()` → helper for tools

### IAgent Interface Evolution

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

    // Lifecycle
    Task CancelAsync(CancellationToken ct);
}
```

## 5. Behavior Migration — Phase B: Events + Streams

### Agent.Events.cs (partial)

- `PublishAsync(IEvent)` → type-safe event publishing
- `HandleEvent(IEvent)` → virtual handler
- `GetEventLogAsync()` → read event log
- Events stored in `IDurableList<AgentEvent>` (serialized from typed messages)

### Agent.Streams.cs (partial)

- Auto-subscribe to `IStreamConsumer<T>` interfaces on activation
- `PublishToStreamAsync<T>(T message)` → publish typed event to Orleans stream
- `GetActiveSubscriptionsAsync()` → list subscribed stream names
- Stream name derived from message type

### IEventDrivenAgent (optional interface)

```csharp
public interface IEventDrivenAgent : IAgent
{
    Task HandleEventAsync(IEvent message, CancellationToken ct);
    Task<IReadOnlyList<AgentEvent>> GetEventLogAsync(CancellationToken ct);
}
```

## 6. Behavior Migration — Phase C: Tracking

### Agent.Tracking.cs (partial)

- `StartTrackingAsync(TrackingItem)` → schedule DurableJob
- `StopTrackingAsync(string id)` → cancel job
- `OnTrackingDueAsync(TrackingItem)` → virtual callback
- Built-in tools: StartTracking, StopTracking, ListTracking

### ITrackableAgent (optional interface)

```csharp
public interface ITrackableAgent : IAgent
{
    Task StartTrackingAsync(string name, TrackingItem item, TimeSpan interval, CancellationToken ct);
    Task StopTrackingAsync(string name, CancellationToken ct);
}
```

## 7. Behavior Migration — Phase D: Observers

Phase 2 — observer pattern for grain-to-grain notifications. Placeholder in V3 matching source.

## 8. Real-World Use Cases (for documentation + samples)

### UC1: Code Review Bot
- Implements: `IStreamConsumer<CodeChangedEvent>`, `INotifier<ReviewRequestNotification>`
- Flow: Subscribes to code changes → analyzes diff → notifies developer
- Demonstrates: Stream consumption, notification publishing, tool usage (file read)

### UC2: Infrastructure Monitor
- Implements: `IStreamProducer<HealthCheckEvent>`, `INotifier<AlertNotification>`, `ITrackableAgent`
- Flow: Tracks service health every 5 min → publishes events → alerts on degradation
- Demonstrates: Tracking, event publishing, notification

### UC3: Personal Assistant (Orchestrator)
- Implements: `IStreamConsumer<ProgressNotification>`, `IBroadcaster<AssignTaskCommand>`
- Flow: Receives user request → decomposes → broadcasts tasks → collects progress
- Demonstrates: Command broadcasting, progress aggregation, conversation

### UC4: Knowledge Base
- Implements: only base `IAgent` (conversation + tools)
- Flow: Answers questions from indexed documents using context providers
- Demonstrates: Minimal agent, context providers, tool usage

### UC5: CI/CD Pipeline
- Implements: `IStreamConsumer<CodeChangedEvent>`, `IStreamProducer<BuildCompletedEvent>`, `INotifier<AlertNotification>`
- Flow: Code change → build → test → deploy → notify
- Demonstrates: Event chains, typed pipeline, multi-stream composition

## 9. Stream Patterns

### Pattern 1: Event Chain (Pipeline)
```
GitAgent ──CodeChangedEvent──→ CIAgent ──BuildCompletedEvent──→ DeployAgent
```
Pipeline emerges from type declarations. No orchestrator needed.

### Pattern 2: Fan-Out (Broadcast)
```
                          ┌──→ ReviewAgent  (IStreamConsumer<CodeChangedEvent>)
GitAgent ─CodeChangedEvent┼──→ CIAgent     (IStreamConsumer<CodeChangedEvent>)
                          └──→ DocsAgent   (IStreamConsumer<CodeChangedEvent>)
```
Adding a consumer = implementing the interface. Zero publisher changes.

### Pattern 3: Fan-In (Aggregation)
```
CIAgent ──BuildCompletedEvent──┐
TestAgent ──TestResultEvent────┼──→ DashboardAgent
DeployAgent ──DeployEvent──────┘
```
Dashboard implements multiple `IStreamConsumer<T>` — one handler per type.

## 10. DynamicAgent

The single runtime-configured agent type:

```csharp
public class DynamicAgent : Agent, IDynamicAgent
{
    public async Task ConfigureAsync(AgentConfiguration config, CancellationToken ct)
    {
        // Set display name, system prompt, tools, workspace
        // Subscribe to streams by string name (only here!)
    }
}
```

`AgentConfiguration` supports string-based stream subscriptions — the only exception to type-safe composition.

## 11. Testing Strategy

Each behavior gets:
1. **Contract tests** — inherited via `AgentTest<T>` base class (declare interface, get tests)
2. **Behavior-specific tests** — e.g., stream delivery, tracking intervals
3. **Integration tests** — full Aspire silo, cross-agent streams
4. **Sample agent tests** — each use case has a working test

## 12. Documentation Plan

VitePress site sections:
1. **Getting Started** — minimal agent in 5 minutes
2. **Core Concepts** — Agent, Behaviors, Messages, Streams
3. **Behaviors Guide** — one page per behavior with examples
4. **Message Types** — ICommand vs IEvent vs INotification
5. **Stream Patterns** — pipeline, fan-out, fan-in with diagrams
6. **Use Cases** — 5 complete walkthroughs
7. **API Reference** — generated from code
8. **Migration Guide** — V2 → V3
