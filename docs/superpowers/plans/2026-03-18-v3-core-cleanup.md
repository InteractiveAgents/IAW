# v3 Core Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clean up the Core library and codebase before merging v3 to master — constants, dead code removal, namespace fixes, test fixes.

**Architecture:** Replace magic strings with typed constants in `IAWConstants`, delete obsolete DynamicAgent/AgentKind, rename base classes to fix `global::` qualifiers, fix Orleans serialization issue in FormTests.

**Tech Stack:** .NET 11, Orleans, C# preview features

**Spec:** `docs/superpowers/specs/2026-03-18-v3-cleanup-design.md`

---

### Task 1: Expand IAWConstants with Events, GrainTypes, StateKeys

**Files:**
- Modify: `src/Core/IAWConstants.cs`

- [ ] **Step 1: Rewrite IAWConstants.cs**

Replace the entire file:

```csharp
namespace Core;

public static class IAWConstants
{
    public const string StreamProvider = "agents";

    public static class Events
    {
        public const string ApprovalRequested = "approval.requested";
        public const string TrackingChanged = "tracking.changed";
        public const string DashboardChanged = "dashboard.changed";
        public const string JobCompleted = "job.completed";
        public const string OrchestrationProgress = "orchestration.progress";
        public const string OrchestrationCompleted = "orchestration.completed";
    }

    public static class GrainTypes
    {
        public const string Agent = "agent";
        public const string Project = "project";
        public const string CodeOrchestrator = "code-orchestrator";
        public const string UserProfile = "user-profile";
        public const string UISession = "ui-session";
        public const string AgentRegistry = "agent-registry";
    }

    public static class StateKeys
    {
        public const string SetupComplete = "setup-complete";
        public const string GroupChatId = "group-chat-id";
        public const string ScheduledDashboardMsgId = "scheduled-dashboard-msgid";
    }
}
```

- [ ] **Step 2: Build Core**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded (old `AgentGrainType` reference may break — fix in next steps).

- [ ] **Step 3: Commit**

```bash
git add src/Core/IAWConstants.cs
git commit -m "feat: expand IAWConstants with Events, GrainTypes, StateKeys"
```

---

### Task 2: Replace all magic strings with constants

**Files:**
- Modify: `src/Core/Agents/Agent.cs` — `[GrainType("agent-v3")]` → `[GrainType(IAWConstants.GrainTypes.Agent)]`
- Modify: `src/Core/Agents/Agent.Events.cs` — `StreamId.Create(IAWConstants.StreamProvider, eventName)` (already uses StreamProvider)
- Modify: `src/Core/Agents/Agent.Tracking.cs` — `"tracking.changed"` → `IAWConstants.Events.TrackingChanged`
- Modify: `src/Agents/Projects/Project.cs` — `"dashboard.changed"`, `"job.completed"`, `"approval.requested"`, `[GrainType("project-v1")]`, `GrainId.Create("code-orchestrator-v1", "code-orchestrator")`
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs` — `[GrainType("code-orchestrator-v1")]`
- Modify: `src/Agents/UserProfile/UserProfile.cs` — `[GrainType("user-profile-v1")]`
- Modify: `src/Agents/UI/UISession.cs` — `[GrainType("ui-session-v1")]`
- Modify: `src/Core/Registry/AgentRegistryGrain.cs` — `[GrainType("agent-registry")]`
- Modify: `src/Clients.Telegram/StreamSubscriber.cs` — event name strings → constants, add local `TelegramEvents` class for `notification.sent`, `wizard.started`
- Modify: `src/Clients.Telegram/TelegramBotService.cs` — `"setup-complete"`, `"group-chat-id"`, `"scheduled-dashboard-msgid"` → `IAWConstants.StateKeys.*`

- [ ] **Step 1: Replace grain type attributes across all files**

For each `[GrainType("...-v1")]` or `[GrainType("...-v3")]`, replace with `[GrainType(IAWConstants.GrainTypes.Xxx)]`. Drop version suffixes — the constant values have no `-v1`/`-v3`.

Also fix `GrainId.Create("code-orchestrator-v1", "code-orchestrator")` in Project.cs to use the constant.

- [ ] **Step 2: Replace event name strings in Core**

In `Agent.Tracking.cs`, replace `"tracking.changed"` with `IAWConstants.Events.TrackingChanged`.

- [ ] **Step 3: Replace event name strings in Agents**

In `Project.cs`, replace:
- `"dashboard.changed"` → `IAWConstants.Events.DashboardChanged`
- `"job.completed"` → `IAWConstants.Events.JobCompleted`
- `"approval.requested"` → `IAWConstants.Events.ApprovalRequested`

- [ ] **Step 4: Replace strings in Telegram client**

In `StreamSubscriber.cs`:
- Add local static class `TelegramEvents` with `NotificationSent = "notification.sent"` and `WizardStarted = "wizard.started"`
- Replace core event strings with `IAWConstants.Events.*`
- Replace Telegram-specific strings with `TelegramEvents.*`

In `TelegramBotService.cs`:
- Replace `"setup-complete"`, `"group-chat-id"`, `"scheduled-dashboard-msgid"` with `IAWConstants.StateKeys.*`

- [ ] **Step 5: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: 0 compilation errors (file-lock warnings OK).

- [ ] **Step 6: Run tests**

Run: `dotnet test test/Core.Tests --verbosity quiet`
Expected: Same pass/fail count as before (no regressions).

- [ ] **Step 7: Commit**

```bash
git add -u
git commit -m "refactor: replace all magic strings with IAWConstants"
```

---

### Task 3: Delete DynamicAgent, AgentKind, orphaned messages

**Files:**
- Delete: `src/Core/Agents/DynamicAgent.cs`
- Delete: `src/Core/Contracts/IDynamicAgent.cs`
- Delete: `src/Core/Contracts/AgentConfiguration.cs`
- Delete: `src/Agents/Messages/DeployFailedMessage.cs`
- Delete: `src/Agents/Messages/DeploySucceededMessage.cs`
- Delete: `src/Agents/Messages/TaskCompletedMessage.cs`
- Delete: `src/Agents/Messages/TaskFailedMessage.cs`
- Delete: `src/Agents/Messages/ReviewFeedbackMessage.cs`
- Delete: `src/Agents/Messages/SpecReadyEvent.cs`
- Delete: `src/Agents/Messages/BuildMetricsCollectedEvent.cs`
- Delete: `src/Agents/Messages/CodeChangedEvent.cs` (empty file)
- Delete: `demo/` directory
- Modify: `src/Core/Agents/Agent.cs` — delete cumulative token state tracking (lines 236-239 and `GetLongFromState` helper) — redundant with OTel metrics
- Modify: `src/Core/Contracts/AgentMetadata.cs` — remove `AgentKind Kind` field, delete `AgentKind` enum
- Modify: `src/Core/Registry/AgentRegistration.cs` — remove `Kind` field
- Modify: `src/Core/Registry/AgentQuery.cs` — remove `Kind` field
- Modify: `src/Core/Registry/AgentRegistrationStartupTask.cs` — remove `DynamicAgent` reference and `Kind` from `BuildRegistration`
- Modify: `src/Core/Agents/Agent.Lifecycle.cs` — remove `AgentKindValue` property, remove `AgentKindValue` from `GetMetadata()` call
- Modify: `src/Core/Orchestration/InterfaceCatalog.cs` — remove `DynamicAgentInterface` exclusion
- Modify: `src/IAW.MCP/Tools/AgentTools.cs` — remove DynamicAgent fallback
- Modify: `src/DevUI/OrleansAgentChatClient.cs` — remove DynamicAgent fallback
- Modify: `src/DevUI/AgentDiscovery.cs` — remove DynamicAgent exclusion
- Modify: `src/Agents/Review/SelfImprovementAgent.cs` — remove `AgentKindValue` override
- Modify: `src/Agents.CSharp/RoslynAgent.cs` — remove `AgentKindValue` override

- [ ] **Step 1: Delete files**

Delete all 8 files listed above plus `demo/` directory.

- [ ] **Step 2: Remove AgentKind from AgentMetadata**

In `AgentMetadata.cs`, remove the `Kind` field and re-number the remaining `[Id]` attributes. Delete the `AgentKind` enum.

```csharp
[GenerateSerializer]
public record AgentMetadata(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] string[] Publishes,
    [property: Id(4)] string[] Subscribes);
```

- [ ] **Step 3: Remove Kind from registry types**

In `AgentRegistration.cs`, remove `Kind` field and re-number IDs.
In `AgentQuery.cs`, remove `Kind` field and re-number IDs.

- [ ] **Step 4: Update AgentRegistrationStartupTask**

Remove `DynamicAgent` import, remove `Kind` from `BuildRegistration`:

```csharp
return new AgentRegistration(
    type.Name,
    GetAgentShortName(type.Name),
    "",
    pubs, subs);
```

- [ ] **Step 5: Update Agent.Lifecycle.cs**

Remove `AgentKindValue` property. Update `GetMetadata()`:

```csharp
return Task.FromResult(new AgentMetadata(
    type.Name, DisplayName, Instructions,
    DiscoverPublishedMessageTypes(type), DiscoverReceivedMessageTypes(type)));
```

- [ ] **Step 6: Update InterfaceCatalog.cs**

Remove `DynamicAgentInterface` field and its exclusion in `Discover()`.

- [ ] **Step 7: Update MCP and DevUI**

In `AgentTools.cs` — remove `if (agentId.StartsWith("dynamic-"))` fallback block.
In `OrleansAgentChatClient.cs` — remove DynamicAgent fallback, simplify to throw for unknown agents.
In `AgentDiscovery.cs` — remove `IDynamicAgent` exclusion.

In `Agent.cs` — delete the cumulative token state tracking block (`cumulative-input-tokens`, `cumulative-output-tokens`, `GetLongFromState` helper). OTel metrics already capture this via `TotalInputTokens`/`TotalOutputTokens` counters.

- [ ] **Step 8: Remove AgentKindValue overrides**

In `SelfImprovementAgent.cs` — delete the `AgentKindValue` override line.
In `RoslynAgent.cs` — delete the `AgentKindValue` override line.

- [ ] **Step 9: Build and test**

Run: `dotnet build IAW.slnx`
Run: `dotnet test test/Core.Tests --verbosity quiet`
Expected: 0 compilation errors, no test regressions.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "refactor: delete DynamicAgent, AgentKind, and 5 orphaned messages"
```

---

### Task 4: Rename Memory → MemoryAgentBase, LLM → LlmAgentBase, fix global::

**Files:**
- Modify: `src/Core/Memory.cs` — rename class `Memory` → `MemoryAgentBase`
- Modify: `src/Core/LLM.cs` — rename class `LLM` → `LlmAgentBase`
- Modify: `src/Core/Agents/Agent.cs` — add `using Core.Context;`, remove `global::` qualifiers
- Modify: `src/Agents/Review/ReviewerAgent.cs` — add `using Core.Context;`, remove `global::`
- Modify: 5 memory agents in `src/Agents/Memory/` — `: global::Core.Memory(` → `: MemoryAgentBase(`
- Modify: 11 LLM agents in `src/Agents/LLM/` — `: global::Core.LLM(` → `: LlmAgentBase(`

- [ ] **Step 1: Rename Memory class**

In `src/Core/Memory.cs`, change `public abstract class Memory` → `public abstract class MemoryAgentBase`. Keep everything else.

- [ ] **Step 2: Rename LLM class**

In `src/Core/LLM.cs`, change `public abstract class LLM` → `public abstract class LlmAgentBase`. Keep everything else.

- [ ] **Step 3: Update 5 memory agents**

In each of `EpisodeMemoryAgent.cs`, `UserMemoryAgent.cs`, `CodeMemoryAgent.cs`, `PatternMemoryAgent.cs`, `ProjectMemoryAgent.cs`:

Replace `: global::Core.Memory(` with `: MemoryAgentBase(`

- [ ] **Step 4: Update 11 LLM agents**

In each LLM agent file, replace `: global::Core.LLM(` with `: LlmAgentBase(`

- [ ] **Step 5: Fix remaining global:: in Agent.cs**

Add `using Core.Context;` to `src/Core/Agents/Agent.cs`. Replace:
- `global::Core.Context.IAgentContextProvider` → `IAgentContextProvider`
- `global::Core.Contracts.TextContent` → `TextContent` (add `using Core.Contracts;` if not present)

- [ ] **Step 6: Fix global:: in ReviewerAgent.cs**

Add `using Core.Context;`, replace `global::Core.Context.IAgentContextProvider` → `IAgentContextProvider`.

- [ ] **Step 7: Verify zero global:: remaining**

Run: `grep -r "global::" src/ --include="*.cs"`
Expected: No matches.

- [ ] **Step 8: Build and test**

Run: `dotnet build IAW.slnx`
Run: `dotnet test test/Core.Tests --verbosity quiet`

- [ ] **Step 9: Commit**

```bash
git add -u
git commit -m "refactor: rename Memory/LLM base classes, remove all global:: qualifiers"
```

---

### Task 5: Fix FormTests — Orleans serialization issue

**Files:**
- Modify: `src/Core/Contracts/UI/` — find types returning `ReadOnlyArray` via collection expressions and change to explicit arrays

**Root cause:** Orleans can't serialize `<>z__ReadOnlyArray'1[Core.Contracts.UI.Button]` — produced by C# collection expressions `[..]` in return types. Fix: use `Button[]` or `List<Button>` explicitly.

- [ ] **Step 1: Find the source of ReadOnlyArray**

Run: `grep -rn "\[.*\]" src/Core/Contracts/UI/ --include="*.cs"` to find collection expressions in UI types that produce `ReadOnlyArray`.

Look for patterns like `return [button1, button2]` or `Buttons = [..]` in Form/Widget types.

- [ ] **Step 2: Replace collection expressions with explicit array types**

Change `return [item1, item2]` to `return new[] { item1, item2 }` or `return new Button[] { item1, item2 }` for any property/method that returns data Orleans needs to serialize.

- [ ] **Step 3: Run FormTests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~FormTests" --verbosity quiet`
Expected: All 17 tests pass (was 1 pass, 16 fail).

- [ ] **Step 4: Review each test for meaningfulness**

Read every test in the FormTests class. Delete any that just set a value and verify it was set. Keep tests that verify state transitions or behavioral contracts.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test test/Core.Tests --verbosity quiet`
Expected: 0 new failures.

- [ ] **Step 6: Commit**

```bash
git add -u
git commit -m "fix: FormTests Orleans serialization — use explicit arrays instead of collection expressions"
```

---

### Task 6: Final build, test, push

- [ ] **Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test test/Core.Tests --verbosity quiet`
Expected: All tests pass (including previously broken FormTests).

- [ ] **Step 3: Verify no magic strings remain for core events**

Run: `grep -rn '"dashboard.changed"\|"job.completed"\|"approval.requested"\|"tracking.changed"\|"orchestration.progress"\|"orchestration.completed"' src/ --include="*.cs"`
Expected: No matches (all replaced with constants).

- [ ] **Step 4: Verify no global:: remains**

Run: `grep -r "global::" src/ --include="*.cs"`
Expected: No matches.

- [ ] **Step 5: Verify no AgentKind references remain**

Run: `grep -r "AgentKind\|AgentKindValue\|DynamicAgent" src/ --include="*.cs"`
Expected: No matches.

- [ ] **Step 6: Push**

```bash
git push origin v3
```
