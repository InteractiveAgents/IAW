# Agent Unification Design

Merge `OrleansAgentGrain` and internal `Agent` into a single public `Agent` class.

## Problem

Two agent hierarchies exist that don't share code:

- `OrleansAgentGrain` — Orleans `DurableGrain` with journaled state but no LLM, no generic tools, no observability
- `Agent` (internal) — rich design with LLM streaming, generic tools, observability, diagnostics — but not an Orleans grain

`TelegramBotGrain` extends `OrleansAgentGrain` and has to manually forward to `IAgent("personal-assistant").SendDeterministicAsync()` because the grain itself can't talk to an LLM.

## Design

### Single `Agent` class (public)

```
Agent : DurableGrain, IAgent, IRemindable
```

Combines:
- **Durable state** from `OrleansAgentGrain` — `[Memory("name")]` journaled collections
- **LLM integration** from old `Agent` — `IChatClient`, `Activate(IChatClient)`, streaming `SendAsync`
- **Generic tools** from old `Agent` — `DefineTools()`, `InvokeToolAsync(name, args)`
- **Observability** from old `Agent` — `ActivitySource`, `Meter`, diagnostics

### IAgent — 7 behavior interfaces (was 9)

Dropped:
- `IAgentConfigurationBehavior` — over-engineered. Tools-enabled is a state key. System prompt is a property. MaxResponseChunks and PromptPrefix unnecessary.
- `SendDeterministicAsync` removed from `IAgentHistoryBehavior` — was a placeholder echo. Real `SendAsync` with LLM replaces it.

Remaining:
1. `IAgentMetadataBehavior` — identity and capabilities
2. `IAgentStateBehavior` — key/value store + increment
3. `IAgentHistoryBehavior` — conversation log (AddHistory, GetHistory)
4. `IAgentEventsBehavior` — event log and publish
5. `IAgentNotificationsBehavior` — cross-agent pub/sub messaging
6. `IAgentTrackingBehavior` — periodic timer/reminder loop
7. `IAgentToolsBehavior` — generic `InvokeToolAsync(string name, Dictionary<string, string> args)`
8. `IAgentStreamsBehavior` — Orleans streaming

### Unified types

| Old | New |
|-----|-----|
| `OrleansAgentMetadata` + `AgentMetadata` | `AgentMetadata` `[GenerateSerializer]` |
| `OrleansAgentHistoryEntry` + `AgentHistoryEntry` | `AgentHistoryEntry` `[GenerateSerializer]` |
| `OrleansAgentEventRecord` + `AgentEvent` | `AgentEvent` `[GenerateSerializer]` |
| `OrleansAgentTrackingStatus` + `TrackingStatus` | `TrackingStatus` `[GenerateSerializer]` |
| `OrleansAgentNotificationEnvelope` | `NotificationEnvelope` `[GenerateSerializer]` |
| `OrleansAgentNotificationRecord` | `NotificationRecord` `[GenerateSerializer]` |
| `OrleansAgentConfig` / `OrleansAgentConfigPatch` | Deleted |
| `AgentConfig` / `AgentConfigPatch` | Deleted |
| `IOrleansAgentGrain` | Deleted (use `IAgent` directly) |
| `AgentSession` | Deleted (history list replaces it) |

### Files deleted

- `OrleansAgentGrain.cs`
- `OrleansAgentContracts.cs` (content moves to new contract files)
- Old `Agent.cs` (replaced by merged Agent)
- `AgentConfig.cs`
- `AgentSession.cs`

### Files created/modified

- `Agent.cs` — the unified public Agent grain
- `AgentContracts.cs` — all `[GenerateSerializer]` types with clean names
- `IAgent.cs` — updated (drop Config behavior, drop SendDeterministic)
- `IAgentBehaviors.cs` — updated interfaces
- `AgentEvent.cs` — becomes `[GenerateSerializer]` record
- `AgentMetadata.cs` — becomes `[GenerateSerializer]` record
- `TrackingOptions.cs` — keeps TrackingOptions, TrackingStatus becomes serializable

### Consumer updates

- `TelegramBotGrain` — base changes from `OrleansAgentGrain(...)` to `Agent(...)`
- `Agents.Tests` — type renames, drop config tests, drop SendDeterministic tests
- `Integration.Tests` — same type renames, endpoint updates
- `Samples/Program.cs` — drop config/SendDeterministic sample endpoints, update types

### Agent.SendAsync on the grain

The grain gets a new method on `IAgentHistoryBehavior`:

```csharp
IAsyncEnumerable<string> SendAsync(string message, CancellationToken ct = default);
```

This requires the LLM to be activated via `[LlmAttribute<TModel>]` constructor injection. If no LLM is bound, `SendAsync` yields nothing (silent no-op, not an error — allows non-LLM agents like pure event processors).
