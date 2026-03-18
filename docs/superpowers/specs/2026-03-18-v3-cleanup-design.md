# v3 Cleanup Before Merge

## Problem

The v3 branch has 250 commits and 430 changed files. Before merging to master, the codebase needs cleanup: magic strings scattered everywhere, dead code from obsolete patterns, `global::` hacks covering up namespace collisions, broken tests, and outdated website docs that don't show real features with real examples.

## Design — Sub-project 1: Core Cleanup

### 1. Constants

**Core universal events** in `IAWConstants.cs`:

```csharp
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

**Telegram-specific events** stay local in `StreamSubscriber.cs` as a static class.

**Agent-specific events** (like `build.succeeded`, `file.read`) stay as local constants in their agent files.

**Grain types:** Drop ALL version suffixes (`-v3`, `-v1`). No versioned grain types.

All consumers updated to reference constants instead of string literals.

### 2. Dead Code Removal

| Delete | Reason |
|--------|--------|
| `DynamicAgent.cs` + `IDynamicAgent.cs` | Obsolete — CodeOrchestrator replaces dynamic agents |
| `AgentKind` enum | Only existed for Dynamic vs Static distinction |
| `AgentKindValue` property in `Agent.Lifecycle.cs` | No longer needed |
| `AgentConfiguration.cs` | Only used by DynamicAgent |
| `Kind` field from `AgentMetadata` and `AgentRegistration` | Remove with enum |
| `Kind` filter from `AgentQuery` | Remove with enum |
| 5 orphaned messages: `DeployFailedMessage`, `DeploySucceededMessage`, `TaskCompletedMessage`, `TaskFailedMessage`, `ReviewFeedbackMessage` | Never referenced anywhere |
| `demo/` directory | Untracked prototype file, served its purpose |
| DynamicAgent fallback in `AgentTools.cs` | Simplify — throw for unknown agent IDs |
| DynamicAgent fallback in `DevUI/OrleansAgentChatClient.cs` | Same |
| `SelfImprovementAgent.AgentKindValue` override | Removed with enum |
| `RoslynAgent.AgentKindValue` override | Removed with enum |

### 3. Fix `global::` Qualifiers

Root cause: base class names collide with namespaces.

| Current | New |
|---------|-----|
| `Core.Memory` (base class) | `MemoryAgentBase` |
| `Core.LLM` (base class) | `LlmAgentBase` |

Add `using Core.Context;` to files using `IAgentContextProvider` to remove remaining `global::` qualifiers.

**Files affected:**
- `src/Core/Agents/Memory.cs` → rename to `MemoryAgentBase`
- `src/Core/Agents/LLM.cs` → rename to `LlmAgentBase`
- 5 memory agents — update base class
- 11 LLM agents — update base class
- `Agent.cs`, `ReviewerAgent.cs` — add using, remove `global::`

### 4. Fix FormTests

Investigate the 45 FormTests failures — likely a grain/state registration issue in the test fixture. Fix the root cause. Review each test: any that just sets a value and checks it was set gets deleted. All surviving tests must verify a state transition or behavioral contract.

### 5. Files Changed Summary

**Core:**
- `IAWConstants.cs` — expand with Events, GrainTypes, StateKeys
- `Agent.cs` — remove `global::`, reference constants
- `Agent.Lifecycle.cs` — remove `AgentKindValue`, `AgentKind`
- `Agent.Events.cs` — use `IAWConstants.Events.*`
- `Agent.Tracking.cs` — use constants
- `AgentMetadata.cs` — remove `Kind` field, delete `AgentKind` enum
- `DynamicAgent.cs` — DELETE
- `IDynamicAgent.cs` — DELETE
- `AgentConfiguration.cs` — DELETE (if exists)
- `Memory.cs` → rename class to `MemoryAgentBase`
- `LLM.cs` → rename class to `LlmAgentBase`
- `InterfaceCatalog.cs` — remove DynamicAgent exclusion
- `AgentRegistration.cs`, `AgentQuery.cs` — remove Kind

**Agents:**
- All `[GrainType("...-v1")]` / `[GrainType("...-v3")]` → drop version suffix
- All event string literals → local constants or `IAWConstants.Events.*`
- 5 orphaned message files — DELETE
- 5 memory agents — update base class name
- 11 LLM agents — update base class name
- `RoslynAgent.cs` — remove AgentKindValue override
- `SelfImprovementAgent.cs` — remove AgentKindValue override

**Telegram:**
- `StreamSubscriber.cs` — local `TelegramEvents` constants, use `IAWConstants.Events.*` for core events
- `TelegramBotService.cs` — use `IAWConstants.StateKeys.*`

**MCP / DevUI:**
- `AgentTools.cs` — remove DynamicAgent fallback
- `OrleansAgentChatClient.cs` — remove DynamicAgent fallback

**Tests:**
- FormTests — investigate and fix root cause
- Update any tests referencing old grain type strings

## Design — Sub-project 2: Website Documentation

Full update of `website/` directory. Not a "v3 rewrite" — just an accurate reflection of current features.

### Pages to Update
- Architecture overview — Agent base class, partials, durable state via Orleans Journaling
- Communication patterns — GetResponse, IReceiver<T>, Orleans streams
- Aspire integration — hosting extensions, MCP agent
- Telegram client — forum topics, streaming, scheduled jobs
- Code orchestration — CodeOrchestrator pattern
- Testing — AgentTest<TAgent> base class

### Code Examples (high quality, concise, real patterns)

**1. Reactive fan-out: multiple agents react to same event**
```csharp
public class FormatterAgent : Agent, IReceiver<CodeChangedMessage>
{
    public async Task<MessageReceipt> ReceiveAsync(CodeChangedMessage msg, CancellationToken ct)
    {
        await GetResponse($"Run dotnet format on {msg.FilePath}", ct);
        return MessageReceipt.Accepted();
    }
}

public class TestAgent : Agent, IReceiver<CodeChangedMessage>
{
    public async Task<MessageReceipt> ReceiveAsync(CodeChangedMessage msg, CancellationToken ct)
    {
        await GetResponse($"Run tests for {msg.ProjectPath}", ct);
        return MessageReceipt.Accepted();
    }
}
```
Both receive `CodeChangedMessage` simultaneously. No orchestration needed — add a receiver, get automatic reactive behavior.

**2. Self-diagnostics: system monitors itself**
```csharp
await project.ScheduleJob("System Health", TimeSpan.FromMinutes(5),
    "Check all Aspire resources. Report only unhealthy services.", ct);
```
One line. AspireAgent uses MCP tools internally. Unhealthy services get reported to Notifications topic automatically.

**3. Context enrichment: memory injection**
```csharp
protected override IReadOnlyList<IAgentContextProvider> GetContextProviders() =>
[
    new UserContextProvider(GrainFactory),
    new ProjectContextProvider(durableState.Tasks, durableState.Files),
    new RAGContextProvider(qdrant, embeddings)
];
```
Every LLM call automatically enriched with user preferences, project state, and relevant documents. No manual prompt engineering.

**4. Pub/sub: stream-based event distribution**
```csharp
// Producer: publish a typed event
await PublishToStream(new BuildCompletedEvent(ProjectPath, Success: true));

// Consumer: auto-subscribed via interface
public class DeployAgent : Agent, IStreamConsumer<BuildCompletedEvent>
{
    public Task OnStreamEvent(BuildCompletedEvent evt, CancellationToken ct) =>
        GetResponse($"Deploy {evt.ProjectPath} — build was {(evt.Success ? "green" : "red")}", ct);
}
```

Each example: self-contained, working, ~5-10 lines, highlights the pattern.

## Design — Sub-project 3: Final Review & PR Polish

After Sub-projects 1 and 2:
- Full code review of the complete diff
- Any remaining naming/style issues
- PR description update
- Merge readiness confirmation

## Execution Order

1. **Sub-project 1: Core Cleanup** — foundational, everything depends on clean Core
2. **Sub-project 2: Website Documentation** — depends on clean Core for accurate examples
3. **Sub-project 3: Final Review** — last pass before merge
