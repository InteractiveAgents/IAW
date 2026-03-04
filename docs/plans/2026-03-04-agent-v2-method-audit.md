# Agent V2 Method Audit (IAgent + Agent)

## Goal

Redesign `IAgent` and `Agent` from scratch with these constraints:

- No mandatory base constructor memory arguments.
- Smaller core API focused on assistant use-cases.
- Optional capabilities split into explicit opt-in modules.
- Safe incremental migration with compatibility adapters.

## Current Pain Points

1. `IAgent` is a broad composition interface with unrelated concerns (state, notifications, tracking, tools, streams).
2. `Agent` is a monolithic base class handling too many responsibilities.
3. Every derived agent must pass six `[Memory(...)]` constructor dependencies.
4. Several methods are convenience helpers or infrastructure internals exposed as public grain API.

## Method Audit (Current -> V2)

### IAgent Behavior Methods

| Area | Current method | Decision | Why | V2 direction |
|---|---|---|---|---|
| Metadata | `GetMetadataAsync` | Replace | Metadata shape is runtime-oriented, not product-oriented | `GetProfileAsync` returning a stable `AgentProfile` |
| State | `SetStateAsync` | Replace | String-only values are limiting | `SetMemoryAsync<T>` / `SetMemoryJsonAsync` |
| State | `GetStateValueAsync` | Replace | String-only read and key/value leakage | `GetMemoryAsync<T>` |
| State | `GetStateAsync` | Remove from core | Full dump is mostly diagnostic/admin behavior | Move to optional diagnostics behavior |
| State | `IncrementAsync` | Remove | Domain convenience, not core capability | Extension/helper package, not base grain API |
| History | `AddHistoryAsync` | Replace | `(role, content)` is weakly typed | `AppendMessageAsync(AgentMessage)` |
| History | `GetHistoryAsync` | Replace | No paging/filtering | `QueryMessagesAsync(AgentMessageQuery)` |
| Events | `PublishEventAsync` | Replace | Event contracts should be typed and queryable | `AppendEventAsync(AgentEvent)` |
| Events | `GetEventsAsync` | Replace | No filtering/paging | `QueryEventsAsync(AgentEventQuery)` |
| Notifications | `SubscribeAsync` | Move out of core | Pub/sub is collaboration feature, not base assistant primitive | Optional `IAgentSubscriptionsV2` |
| Notifications | `NotifyAsync(string, string)` | Remove | Duplicate overload and weak envelope | Keep only envelope-based publish in optional module |
| Notifications | `NotifyAsync(NotificationEnvelope)` | Move out of core | Optional collaboration concern | Optional `IAgentNotificationsV2.PublishAsync` |
| Notifications | `ReceiveNotificationAsync(string, string)` | Remove from public API | Delivery endpoint should be internal | Internal runtime method only |
| Notifications | `ReceiveNotificationAsync(NotificationEnvelope)` | Remove from public API | Same reason | Internal runtime method only |
| Notifications | `GetNotificationsAsync` | Move out of core | Diagnostic inbox access is optional | Optional `IAgentNotificationsV2.QueryInboxAsync` |
| Tracking | `StartTrackingAsync` | Move out of core | Scheduling is infrastructure concern | Optional `IAgentSchedulingV2` |
| Tracking | `StopTrackingAsync` | Move out of core | Scheduling is infrastructure concern | Optional `IAgentSchedulingV2` |
| Tracking | `GetTrackingStatusAsync` | Move out of core | Scheduling is infrastructure concern | Optional `IAgentSchedulingV2` |
| Tools | `InvokeToolAsync` | Remove from grain API | String-based remote tool invocation leaks internals | Tool runtime is local/internal to `RespondAsync` |
| Streams | `PublishStreamAsync` | Move out of core | Transport-specific and generic messaging concern | Optional streaming behavior/integration package |

### Agent Base Class Members

| Member | Decision | Why | V2 direction |
|---|---|---|---|
| `Id` | Keep | Core identity primitive | Keep as read-only grain identity |
| `DisplayName` | Replace | Scattered profile fields | Move to `AgentProfile.DisplayName` |
| `SystemPrompt` | Replace | Prompt should be part of behavior profile/config | Move to `AgentProfile.Instructions` |
| `DefineTools()` | Replace | Tool config should be runtime/builder-driven | `ConfigureTools(IToolRegistry)` or profile-level declaration |
| `Activate(IChatClient)` | Remove | Imperative activation is easy to forget and causes state split | Initialize runtime in activation pipeline |
| `SendAsync(string)` | Replace | Weak request contract and history side effects hidden | `RespondAsync(AgentRequest)` (+ optional streaming contract) |
| Tracking-specific reminder/timer methods | Extract | Cross-cutting infra mixed with core logic | Move to scheduling module |
| Notification normalization helpers | Keep as internal utility (module-scoped) | Needed behavior, but not core base concern | Move with notifications module |

## Constructor/Variable Audit

Current mandatory base constructor args in `Agent`:

- `values`
- `history`
- `events`
- `subscriptions`
- `notifications`
- `tracking`

Decision:

- Replace these six mandatory constructor parameters with one runtime-provided state accessor abstraction.
- Derived agents should not pass storage plumbing. They should only define domain behavior and optional dependencies.

Draft runtime shape:

```csharp
public interface IAgentStateStore
{
    IDurableDictionary<string, string> Values { get; }
    IDurableList<AgentMessage> Messages { get; }
    IDurableList<AgentEvent> Events { get; }
}
```

## IAgentV2 Draft 0 (Minimal Core)

```csharp
public interface IAgentV2 : IGrainWithStringKey
{
    Task<AgentProfile> GetProfileAsync(CancellationToken ct = default);

    Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct = default);

    Task AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<AgentMessage>> QueryMessagesAsync(
        AgentMessageQuery? query = null,
        CancellationToken ct = default);

    Task SetMemoryAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetMemoryAsync(string key, CancellationToken ct = default);
}
```

Optional capabilities split from core:

- `IAgentEventsV2`
- `IAgentNotificationsV2`
- `IAgentSubscriptionsV2`
- `IAgentSchedulingV2`
- `IAgentStreamingV2`

## Migration Strategy (Micro-Steps)

1. Add V2 contracts only (`Core/V2/*`) with no runtime behavior changes.
2. Add adapter layer so current `Agent` can satisfy V2 contracts without breaking current tests.
3. Introduce new base implementation (`AgentV2`) with runtime-owned state access (no six-memory-args pattern).
4. Migrate one simple agent (`GitHubTestAgent`) as pilot.
5. Migrate complex grain (`TelegramConversationGrain`).
6. Move optional concerns (tracking, notifications, streams, tools) into separate behaviors/modules.
7. Mark V1 methods `[Obsolete]` and publish migration guide.
8. Remove V1 in the next major version.

## First Implementation Slice (Next Step)

Create V2 contracts only:

- `src/Core/V2/AgentProfile.cs`
- `src/Core/V2/AgentRequest.cs`
- `src/Core/V2/AgentReply.cs`
- `src/Core/V2/AgentMessage.cs`
- `src/Core/V2/AgentMessageQuery.cs`
- `src/Core/V2/IAgentV2.cs`

No behavioral changes in this slice. Validation gate: existing core tests remain green.
