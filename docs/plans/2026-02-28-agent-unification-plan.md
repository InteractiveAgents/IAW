# Agent Unification Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Merge `OrleansAgentGrain` and internal `Agent` into a single public `Agent : DurableGrain` class, unify all contract types, drop Config behavior and `SendDeterministicAsync`.

**Architecture:** Single `Agent` base grain class combining durable journaled state, LLM integration, generic tools, and observability. All `OrleansAgent*` types renamed to clean names. `IAgent` drops `IAgentConfigurationBehavior`. `IAgentToolsBehavior` gets generic `InvokeToolAsync`. `IAgentHistoryBehavior` drops `SendDeterministicAsync`, gains `SendAsync`.

**Tech Stack:** .NET 11, Orleans 10.0, Orleans Journaling (`DurableGrain`), `Microsoft.Extensions.AI`, xunit v3, Aspire 13.1

---

### Task 1: Rename contract types (drop `Orleans` prefix)

**Files:**
- Modify: `src/Core/OrleansAgentContracts.cs` → rename to `src/Core/AgentContracts.cs`
- Modify: `src/Core/OrleansAgentNotificationJson.cs` → rename to `src/Core/NotificationJson.cs`

**Step 1: Rename types in OrleansAgentContracts.cs**

Rename all types by dropping the `Orleans` prefix. Delete `IOrleansAgentGrain`:

| Old name | New name |
|----------|----------|
| `IOrleansAgentGrain` | Delete (use `IAgent`) |
| `OrleansAgentMetadata` | `AgentMetadata` |
| `OrleansAgentHistoryEntry` | `AgentHistoryEntry` |
| `OrleansAgentEventRecord` | `AgentEventRecord` |
| `OrleansAgentNotificationEnvelope` | `NotificationEnvelope` |
| `OrleansAgentNotificationRecord` | `NotificationRecord` |
| `OrleansAgentTrackingStatus` | `AgentTrackingStatus` |
| `OrleansAgentConfig` | Delete entirely |
| `OrleansAgentConfigPatch` | Delete entirely |

Also rename `AgentMetadata.AgentId` to just `Id` since the `Agent` prefix is already in the type name.

**Step 2: Rename the file**

```bash
cd E:/IAW/InteractiveAgents/IAW
git mv src/Core/OrleansAgentContracts.cs src/Core/AgentContracts.cs
```

**Step 3: Update OrleansAgentNotificationJson.cs**

Rename `OrleansAgentNotificationJson` → `NotificationJson`. Update all type references to new names (`NotificationEnvelope`, `NotificationRecord`).

```bash
git mv src/Core/OrleansAgentNotificationJson.cs src/Core/NotificationJson.cs
```

**Step 4: Delete old duplicate types**

Delete files that are now superseded by the unified contracts:
- `src/Core/AgentEvent.cs` — `AgentEvent` record is replaced by `AgentEventRecord` (serializable)
- `src/Core/AgentMetadata.cs` — `AgentMetadata` record is replaced by serializable `AgentMetadata` class
- `src/Core/AgentSession.cs` — session is replaced by the durable history list
- `src/Core/AgentConfig.cs` — `AgentConfig`/`AgentConfigPatch` dropped entirely

**Step 5: Build to check for compile errors**

Run: `dotnet build src/Core/Core.csproj`
Expected: Many compile errors in consumers (will fix in subsequent tasks)

**Step 6: Commit**

```bash
git add -A
git commit -m "refactor: rename Orleans-prefixed contract types to clean names"
```

---

### Task 2: Update IAgent and behavior interfaces

**Files:**
- Modify: `src/Core/IAgent.cs`
- Modify: `src/Core/IAgentBehaviors.cs`

**Step 1: Update IAgent.cs**

Remove `IAgentConfigurationBehavior` from the `IAgent` composition. Keep the other 8 behaviors (7 remaining + Streams).

```csharp
using Orleans;

namespace Core;

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

**Step 2: Update IAgentBehaviors.cs**

Update all behavior interfaces to use the new type names and drop removed features:

```csharp
namespace Core;

public interface IAgentMetadataBehavior
{
    Task<AgentMetadata> GetMetadataAsync(CancellationToken ct = default);
}

public interface IAgentStateBehavior
{
    Task SetStateAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetStateValueAsync(string key, CancellationToken ct = default);
    Task<Dictionary<string, string>> GetStateAsync(CancellationToken ct = default);
    Task<int> IncrementAsync(string counterKey, CancellationToken ct = default);
}

public interface IAgentHistoryBehavior
{
    Task AddHistoryAsync(string role, string content, CancellationToken ct = default);
    Task<List<AgentHistoryEntry>> GetHistoryAsync(CancellationToken ct = default);
}

public interface IAgentEventsBehavior
{
    Task PublishEventAsync(string name, string? payload = null, CancellationToken ct = default);
    Task<List<AgentEventRecord>> GetEventsAsync(CancellationToken ct = default);
}

public interface IAgentNotificationsBehavior
{
    Task SubscribeAsync(string topic, string subscriberAgentId, CancellationToken ct = default);
    Task NotifyAsync(string topic, string payload, CancellationToken ct = default);
    Task NotifyAsync(NotificationEnvelope notification, CancellationToken ct = default);
    Task ReceiveNotificationAsync(string topic, string payload, CancellationToken ct = default);
    Task ReceiveNotificationAsync(NotificationEnvelope notification, CancellationToken ct = default);
    Task<List<NotificationRecord>> GetNotificationsAsync(CancellationToken ct = default);
}

public interface IAgentTrackingBehavior
{
    Task StartTrackingAsync(TimeSpan interval, int maxTicks, CancellationToken ct = default);
    Task StopTrackingAsync(CancellationToken ct = default);
    Task<AgentTrackingStatus> GetTrackingStatusAsync(CancellationToken ct = default);
}

public interface IAgentToolsBehavior
{
    Task<string?> InvokeToolAsync(string toolName, Dictionary<string, string>? arguments = null, CancellationToken ct = default);
}

public interface IAgentStreamsBehavior
{
    Task PublishStreamAsync(string streamNamespace, Guid streamId, string message, CancellationToken ct = default);
}
```

Key changes:
- `IAgentHistoryBehavior`: removed `SendDeterministicAsync`, types renamed
- `IAgentToolsBehavior`: replaced `InvokeAddNumbersToolAsync(int, int)` with generic `InvokeToolAsync(string, Dictionary<string, string>?)`
- `IAgentConfigurationBehavior`: deleted entirely
- All return types use new names

**Step 3: Build Core**

Run: `dotnet build src/Core/Core.csproj`
Expected: Compile errors in `OrleansAgentGrain.cs` and `Agent.cs` (will fix in next task)

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: update IAgent interfaces - drop config, generic tools, unified types"
```

---

### Task 3: Merge OrleansAgentGrain and Agent into unified Agent class

**Files:**
- Delete: `src/Core/OrleansAgentGrain.cs`
- Delete: `src/Core/Agent.cs` (old internal class)
- Create: `src/Core/Agent.cs` (new public grain)

**Step 1: Write the unified Agent class**

Create `src/Core/Agent.cs` that:
- Extends `DurableGrain`, implements `IAgent`, `IRemindable`
- Has all 7 `[Memory("...")]` durable state parameters from `OrleansAgentGrain`
- Has all metadata, state, history, events, notifications, tracking, streams behavior from `OrleansAgentGrain`
- Adds LLM integration from old `Agent`: `Activate(IChatClient)`, `SendAsync(string, CancellationToken)` returning `IAsyncEnumerable<string>`
- Adds generic tools: `virtual DefineTools() => []`, `InvokeToolAsync(name, args)`
- Adds observability from old `Agent`: `ActivitySource`, counters
- Drops: `SendDeterministicAsync`, all config behavior, `IOrleansAgentGrain`

Key design decisions:
- `SendAsync` is NOT on `IAgent` grain interface (streaming `IAsyncEnumerable` doesn't work across grain boundaries). It's a `public virtual` method on the `Agent` class for in-process use. For grain-to-grain communication, agents use notifications.
- `DefineTools()` returns `IReadOnlyList<AITool>`. `InvokeToolAsync` on `IAgentToolsBehavior` finds the named tool and invokes it.
- Constructor takes the 7 durable collections. Subclasses (like `TelegramBotGrain`) pass them through.
- `virtual string SystemPrompt => string.Empty` and `virtual string DisplayName => Id` for overrides.

**Step 2: Delete old files**

```bash
rm src/Core/OrleansAgentGrain.cs
```

(Old `Agent.cs` is overwritten by the new one)

**Step 3: Build Core**

Run: `dotnet build src/Core/Core.csproj`
Expected: PASS (Core compiles with the new unified Agent)

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: merge OrleansAgentGrain and Agent into single public Agent class"
```

---

### Task 4: Update TelegramBotGrain

**Files:**
- Modify: `src/Clients.Telegram.Bot/TelegramBotGrain.cs`
- Modify: `src/Clients.Telegram.Bot/ITelegramBot.cs` (if type refs need updating)

**Step 1: Update base class and type references**

Change:
- `OrleansAgentGrain(values, history, ...)` → `Agent(values, history, ...)`
- `IOrleansAgentGrain` → `IAgent` (if referenced)
- Any `OrleansAgent*` type references → new names

**Step 2: Build TelegramBot project**

Run: `dotnet build src/Clients.Telegram.Bot/TelegramBot.csproj`
Expected: PASS

**Step 3: Commit**

```bash
git add -A
git commit -m "refactor: update TelegramBotGrain to use unified Agent base class"
```

---

### Task 5: Update Samples project

**Files:**
- Modify: `samples/Samples/Program.cs`

**Step 1: Remove endpoints that used dropped features**

Delete these endpoints entirely:
- `/samples/orleans-agent/history` — used `SendDeterministicAsync`
- `/samples/orleans-agent/configure` — used `ConfigureAsync`, `SendDeterministicAsync`, `InvokeAddNumbersToolAsync`
- `/samples/orleans-agent/tool` — used `InvokeAddNumbersToolAsync`
- `/samples/agent/send-empty` — used `ConfigureAsync`, `SendDeterministicAsync`
- `/samples/agent/system-prompt` — used `GetConfigurationAsync`
- `/samples/agent/activate-default` — used `GetConfigurationAsync`
- `/samples/agent/activate-custom` — used `ConfigureAsync`
- `/samples/agent/history` — used `SendDeterministicAsync`
- `/samples/agent/tools-default` — used `ConfigureAsync`, `GetConfigurationAsync`
- `/samples/agent/tools-custom` — used `GetConfigurationAsync`
- `/samples/agent/tool-call` — used `InvokeAddNumbersToolAsync`
- `/samples/agent/diagnose` — used `SendDeterministicAsync`, `InvokeAddNumbersToolAsync`
- `/samples/agent/configure` — used `ConfigureAsync`, `SendDeterministicAsync`, `InvokeAddNumbersToolAsync`, `GetConfigurationAsync`

**Step 2: Update remaining endpoints for type renames**

- `OrleansAgentNotificationEnvelope` → `NotificationEnvelope`
- `OrleansAgentNotificationJson` → `NotificationJson`
- `OrleansAgentTrackingStatus` → `AgentTrackingStatus`
- `OrleansAgentMetadata` → `AgentMetadata`
- `AgentMetadata` usage in `/samples/agent/metadata` — update the `ToLegacyMetadata` helper or remove it

Also update `WaitForOrleansTrackingToStopAsync` return type to `AgentTrackingStatus`.

**Step 3: Build Samples**

Run: `dotnet build samples/Samples/Samples.csproj`
Expected: PASS

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: update samples for unified agent types, remove dropped endpoints"
```

---

### Task 6: Update unit tests (Agents.Tests)

**Files:**
- Modify: `test/Agents.Tests/OrleansAgentGrainBehaviorTests.cs`
- Modify: `test/Agents.Tests/ArchitectureGuardTests.cs`

**Step 1: Update OrleansAgentGrainBehaviorTests**

- Delete `SendDeterministic_WritesHistory` test
- Delete `Configure_CanDisableResponsesAndTools` test
- Rename all `OrleansAgent*` types → new names throughout
- Update `WaitForTrackingToStopAsync` return type from `OrleansAgentTrackingStatus` → `AgentTrackingStatus`

**Step 2: Update ArchitectureGuardTests**

The guard tests need significant rework since `Agent` is now public:
- `CoreAssembly_DoesNotExposeLegacyAgentClassesPublicly` — `Agent` IS now public. This test assertion `Assert.False(legacyAgentType!.IsPublic)` must flip to `Assert.True`. Remove the "legacy" framing. Update to verify `Agent` extends `DurableGrain`.
- Keep the legacy channel streaming guards (still valid).
- Keep the legacy type guards (still valid, different type names).

**Step 3: Run unit tests**

Run: `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj`
Expected: All tests PASS

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: update unit tests for unified agent"
```

---

### Task 7: Update integration tests

**Files:**
- Modify: `test/Integration.Tests/OrleansAgentIntegrationTests.cs`

**Step 1: Remove tests for dropped endpoints**

Delete assertions for removed endpoints:
- `/samples/orleans-agent/history` assertions
- `/samples/orleans-agent/configure` assertions
- `/samples/orleans-agent/tool` assertions
- All `/samples/agent/send-empty`, `system-prompt`, `activate-default`, `activate-custom`, `history`, `tools-default`, `tools-custom`, `tool-call`, `diagnose`, `configure` assertions from `OrleansSampleEndpoints_ReportExpectedBehavior`

**Step 2: Remove tests that used dropped APIs**

- `OrleansClient_StateAndHistory_PersistForSameAgentIdAcrossCalls` — uses `SendDeterministicAsync`. Rewrite to use `AddHistoryAsync` directly instead.

**Step 3: Rename all types**

- `OrleansAgentNotificationEnvelope` → `NotificationEnvelope`
- `OrleansAgentNotificationRecord` → `NotificationRecord`
- `OrleansAgentNotificationJson` → `NotificationJson`

**Step 4: Run integration tests**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`
Expected: All tests PASS (note: this boots the full Aspire app, takes ~1 minute)

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: update integration tests for unified agent"
```

---

### Task 8: Full build and test verification

**Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: PASS, zero warnings related to our changes

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All tests PASS

**Step 3: Commit any remaining fixups**

If any issues found, fix and commit.

**Step 4: Final commit**

```bash
git add -A
git commit -m "chore: agent unification complete - all tests passing"
```
