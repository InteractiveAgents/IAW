# Migration Guide: V2 to V3

Audit date: 2026-03-07

## Interface Comparison

### IAgentV2 vs IAgent (V3)

| V2 Method | V3 Equivalent | Notes |
|-----------|---------------|-------|
| `GetProfileAsync` | `GetMetadataAsync` + `GetCapabilitiesAsync` | Profile split into metadata (identity) and capabilities (feature flags). |
| `RespondAsync(AgentRequest)` | `GetResponse(string)` / `GetResponseStream(string)` | Simplified to string prompt. Streaming is first-class. |
| `AppendMessageAsync` | Automatic via `DurableChatHistoryProvider` | Messages stored automatically during conversation. |
| `QueryMessagesAsync` | `GetHistory()` | Returns full history. No query/filter. |
| `SetMemoryAsync(key, value)` | `Agent.State[key] = new StateEntry(key, value)` | Direct dictionary access (protected). Not on interface. |
| `GetMemoryAsync(key)` | `GetStateAsync()` | Returns full state snapshot. |
| `AppendEventAsync` | `HandleEventAsync(AgentEvent)` | Events are handled, not just appended. |
| `QueryEventsAsync` | `GetEventLogAsync()` | Returns full log. No query/filter. |
| `SubscribeAsync(topic, subscriberId)` | `IStreamConsumer<TEvent>` interface | Type-safe stream subscriptions via interface implementation. |
| `NotifyAsync(envelope)` | `INotifier<T>.NotifyAsync` / `IBroadcaster<T>.BroadcastAsync` | Typed notification/broadcast patterns. |
| `ReceiveNotificationAsync` | `IReceiver<T>.ReceiveAsync` | Typed message receipt with accept/reject. |
| `QueryNotificationsAsync` | Removed | Use event log or observer pattern instead. |
| `StartScheduleAsync` | `StartTrackingAsync` (via `ITrackableAgent`) | Renamed. Tracking items carry description + interval. |
| `StopScheduleAsync` | `StopTrackingAsync` | Renamed. |
| `GetScheduleStatusAsync` | Removed | Check tracking items via state. |
| `PublishStreamAsync(ns, id, msg)` | `PublishToStreamAsync(AgentEvent)` | Structured events instead of raw strings. |
| `InvokeToolAsync(name, args)` | Automatic via `Microsoft.Extensions.AI` | Tools registered as `AITool` instances; LLM invokes them directly. |
| - | `CancelAsync` | New: cooperative cancellation. |
| - | `SetWorkspaceAsync` | New: workspace directory for file/shell tools. |
| - | `ClearHistoryAsync` | New: reset conversation. |
| - | `GetActiveSubscriptionsAsync` | New: list stream subscriptions. |

## Architecture Changes

### State Management
- **V2**: Single `DurableGrain` with all state managed internally by `AgentV2` base class. Derived agents only override `Profile` and `OnRespondAsync`.
- **V3**: Explicit `[Memory]` constructor parameters for each store (`state`, `eventLog`, `history`, `trackingItems`). State is exposed as protected properties.

### LLM Integration
- **V2**: `AgentV2` calls `IChatClient` directly via `OnRespondAsync`.
- **V3**: Uses `Microsoft.Agents.AI.AIAgent` with `ChatHistoryProvider` pattern. Tools are auto-discovered from `FileTools`, `ShellTools`, `WebTools`, `WorkspaceTools`, and `DefineTools()`.

### Communication
- **V2**: Flat pub/sub via `SubscribeAsync`/`NotifyAsync` with string topics and `NotificationEnvelope`.
- **V3**: Type-safe communication via generic interfaces: `IBroadcaster<T>`, `IReceiver<T>`, `INotifier<T>`, `IStreamConsumer<T>`, `IStreamProducer<T>`.

### Serialization
- **V2**: All contracts in `Core.V2` namespace.
- **V3**: Contracts split across `Core.V3`, `Core.V3.Messages`, `Core.V3.Communication`, `Core.V3.Registry`.

### Grain Types
- **V2**: No explicit `[GrainType]` attributes.
- **V3**: Explicit grain type IDs: `"agent-v3"`, `"dynamic-agent-v3"`, `"agent-registry"`.

## Migration Steps

1. Change base class from `AgentV2` to `Agent` (V3).
2. Add `[Memory]` constructor parameters for all durable stores.
3. Replace `OnRespondAsync` override with `DefineTools()` + `Instructions` property.
4. Replace `GetProfileAsync` with `GetMetadataAsync`/`GetCapabilitiesAsync`.
5. Replace string-based pub/sub with typed `IStreamConsumer<T>` / `IBroadcaster<T>` interfaces.
6. Replace `InvokeToolAsync` with `AITool` registration via `DefineTools()`.
7. Add `[Capability]`, `[Publishes]`, `[Subscribes]` attributes to agent classes for registry discovery.
