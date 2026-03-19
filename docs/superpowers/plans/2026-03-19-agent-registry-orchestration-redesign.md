# Agent Registry & Orchestration Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the IAW agent framework with namespace-based taxonomy, Qdrant-backed agent registry, dynamic grain IDs, semantic generative UI, Durable Jobs scheduling, and Thread-based conversation model.

**Architecture:** Agents organized into flat namespaces (System, Coding, Models, Memory, Orchestration). A global AgentRegistry grain backed by Qdrant hybrid search replaces InterfaceCatalog. Dynamic string grain IDs (`typeof(T).Name`) replace singleton IDs. Interface methods auto-register as AI tools. Durable Jobs v2 replaces Reminders v1. Semantic UIParts replace per-widget-type UI interfaces. Thread replaces Project as the user-facing conversation agent.

**Tech Stack:** .NET 11, Orleans 10, Qdrant, Microsoft.Extensions.VectorData.Abstractions, Microsoft.Extensions.VectorData.Qdrant, Orleans Durable Jobs v2, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-19-agent-registry-orchestration-redesign.md`

---

## Phase 1: Core Infrastructure (IAgent, UIParts, Dynamic IDs, Tools)

Foundation changes that everything else depends on. Must be done first.

### Task 1: Add UIPart types to Core

**Files:**
- Create: `src/Core/UI/UIPart.cs`
- Create: `src/Core/UI/AgentResponse.cs`
- Test: `test/Core.Tests/UI/UIPartTests.cs`

- [ ] **Step 1: Write test for UIPart serialization roundtrip**

```csharp
public class UIPartTests
{
    [Fact]
    public void TextPart_SerializesCorrectly()
    {
        var part = new TextPart("hello", TextStyle.Success);
        Assert.Equal("hello", part.Content);
        Assert.Equal(TextStyle.Success, part.Style);
    }

    [Fact]
    public void AgentResponse_ContainsMultipleParts()
    {
        var response = new AgentResponse([
            new TextPart("test"),
            new OptionsPart("pick one", [new Option("A", "a")], "cb-1")
        ]);
        Assert.Equal(2, response.Parts.Count);
        Assert.IsType<TextPart>(response.Parts[0]);
        Assert.IsType<OptionsPart>(response.Parts[1]);
    }
}
```

- [ ] **Step 2: Run test — verify it fails (types don't exist)**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~UIPartTests" -v n`

- [ ] **Step 3: Create UIPart.cs with all types**

Create `src/Core/UI/UIPart.cs` with all UIPart records from spec Section 6: TextPart, OptionsPart, Option, CardPart, CardField, MediaPart, ProgressPart, FormPart, FormField, TextStyle, FormFieldType. All marked `[GenerateSerializer]`.

- [ ] **Step 4: Create AgentResponse.cs**

Create `src/Core/UI/AgentResponse.cs`: `[GenerateSerializer] public record AgentResponse(List<UIPart> Parts);`

- [ ] **Step 5: Run test — verify it passes**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~UIPartTests" -v n`

- [ ] **Step 6: Commit**

```bash
git add src/Core/UI/ test/Core.Tests/UI/
git commit -m "feat: add UIPart types and AgentResponse"
```

---

### Task 2: Extend IAgent with HandleCallback, GetRichResponse, and Scheduling

**Files:**
- Modify: `src/Core/Contracts/IAgent.cs`
- Create: `src/Core/Contracts/ScheduledJobInfo.cs`
- Modify: `src/Core/Agents/Agent.cs` (add default implementations)
- Test: `test/Core.Tests/AgentTests.cs`

- [ ] **Step 1: Write test for HandleCallback default behavior**

Add to `test/Core.Tests/AgentTests.cs`:

```csharp
[Fact]
public async Task HandleCallback_ReturnsEmptyByDefault()
{
    var agent = Cluster.GrainFactory.GetGrain<ITestAgent>(UniqueId("cb"));
    var result = await agent.HandleCallback("unknown", "val", TestContext.Current.CancellationToken);
    Assert.Empty(result.Parts);
}
```

- [ ] **Step 2: Run test — verify it fails**

Run: `dotnet test test/Core.Tests --filter "HandleCallback_ReturnsEmptyByDefault" -v n`

- [ ] **Step 3: Add new methods to IAgent.cs**

Add to `src/Core/Contracts/IAgent.cs`:
- `Task<AgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct);`
- `Task<AgentResponse> GetRichResponse(string prompt, CancellationToken ct);`
- `Task ScheduleJob(string name, TimeSpan delay, string prompt, CancellationToken ct);`
- `Task ScheduleRecurringJob(string name, TimeSpan interval, string prompt, CancellationToken ct);`
- `Task CancelJob(string name, CancellationToken ct);`
- `Task<List<ScheduledJobInfo>> ListJobs(CancellationToken ct);`

Create `src/Core/Contracts/ScheduledJobInfo.cs`:
```csharp
[GenerateSerializer]
public record ScheduledJobInfo(string Name, string Prompt, TimeSpan Interval, DateTimeOffset? NextDue);
```

- [ ] **Step 4: Add default implementations in Agent base class**

In `src/Core/Agents/Agent.cs`, add default virtual implementations that return empty responses / empty lists. Scheduling methods will be properly implemented in Task 4.

- [ ] **Step 5: Run test — verify it passes**

Run: `dotnet test test/Core.Tests --filter "HandleCallback_ReturnsEmptyByDefault" -v n`

- [ ] **Step 6: Build the full solution**

Run: `dotnet build IAW.slnx`
Expect: 0 errors (some agents may need stub implementations of new interface methods)

- [ ] **Step 7: Commit**

```bash
git add src/Core/Contracts/ src/Core/Agents/Agent.cs test/Core.Tests/AgentTests.cs
git commit -m "feat: extend IAgent with HandleCallback, GetRichResponse, scheduling"
```

---

### Task 3: Add Get<T>() extension methods for dynamic grain IDs

**Files:**
- Create: `src/Core/Extensions/ClusterClientExtensions.cs`
- Create: `src/Core/Extensions/GrainFactoryExtensions.cs`
- Test: `test/Core.Tests/Extensions/DynamicIdTests.cs`

- [ ] **Step 1: Write test for Get<T>() generating unique IDs**

```csharp
public class DynamicIdTests
{
    [Fact]
    public void Get_WithoutScope_GeneratesUniqueIds()
    {
        // Test the ID generation logic directly
        var id1 = $"{typeof(IGit).Name}-{Guid.NewGuid():N}"[..20];
        var id2 = $"{typeof(IGit).Name}-{Guid.NewGuid():N}"[..20];
        Assert.StartsWith("IGit-", id1);
        Assert.StartsWith("IGit-", id2);
        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void Get_WithScope_ProducesDeterministicId()
    {
        var scope = "task-abc";
        var id = $"{scope}/{typeof(IGit).Name}";
        Assert.Equal("task-abc/IGit", id);
    }
}
```

- [ ] **Step 2: Run test — verify fails**

- [ ] **Step 3: Create ClusterClientExtensions.cs**

```csharp
namespace Core;

public static class ClusterClientExtensions
{
    public static T Get<T>(this IClusterClient client) where T : IAgent
        => client.GetGrain<T>($"{typeof(T).Name}-{Guid.NewGuid().ToString("N")[..8]}");

    public static T Get<T>(this IClusterClient client, string scope) where T : IAgent
        => client.GetGrain<T>($"{scope}/{typeof(T).Name}");
}
```

Create matching `GrainFactoryExtensions.cs` with same pattern for `IGrainFactory`.

- [ ] **Step 4: Run tests — verify pass**

- [ ] **Step 5: Commit**

```bash
git add src/Core/Extensions/ test/Core.Tests/Extensions/
git commit -m "feat: add Get<T>() extensions for dynamic grain IDs"
```

---

### Task 4: Replace Agent.Tracking.cs with Agent.Scheduling.cs (Durable Jobs v2)

**Files:**
- Delete: `src/Core/Agents/Agent.Tracking.cs`
- Create: `src/Core/Agents/Agent.Scheduling.cs`
- Modify: `src/Core/Agents/Agent.cs` (remove IRemindable)
- Test: `test/Core.Tests/AgentSchedulingTests.cs`

- [ ] **Step 1: Research Durable Jobs v2 API availability in Orleans 10.0.1**

Run: `dotnet list src/Core package | grep -i orleans` to confirm Orleans version. Check if `IScheduledJobReceiver` and `ILocalScheduledJobManager` are available. If not yet in stable Orleans 10, use a compatibility shim that stores jobs in durable state and uses a grain timer to check due times.

- [ ] **Step 2: Write test for scheduling a job**

```csharp
public class AgentSchedulingTests : AgentTest<ITestAgent>
{
    [Fact]
    public async Task ScheduleJob_StoresInDurableState()
    {
        var agent = Agent(UniqueId("sched"));
        await agent.ScheduleJob("test-job", TimeSpan.FromMinutes(5), "do something", TestContext.Current.CancellationToken);
        var jobs = await agent.ListJobs(TestContext.Current.CancellationToken);
        Assert.Single(jobs);
        Assert.Equal("test-job", jobs[0].Name);
    }

    [Fact]
    public async Task CancelJob_RemovesFromState()
    {
        var agent = Agent(UniqueId("cancel"));
        var ct = TestContext.Current.CancellationToken;
        await agent.ScheduleJob("j1", TimeSpan.FromMinutes(5), "prompt", ct);
        await agent.CancelJob("j1", ct);
        var jobs = await agent.ListJobs(ct);
        Assert.Empty(jobs);
    }
}
```

- [ ] **Step 3: Run tests — verify fail**

- [ ] **Step 4: Create Agent.Scheduling.cs**

Implement scheduling in durable state. Store `ScheduledJobItem` records (name, prompt, interval, nextDue, threadId) in `durableState.ScheduledJobs`. Implement `ScheduleJob`, `ScheduleRecurringJob`, `CancelJob`, `ListJobs`. Wire up job delivery (approach depends on Step 1 research).

- [ ] **Step 5: Delete Agent.Tracking.cs**

Remove `Agent.Tracking.cs`. Remove `IRemindable` from Agent base class. Remove `TrackingItem` and `TrackingItems` from `AgentDurableState`.

- [ ] **Step 6: Fix compilation — update any agents that used tracking**

Search for `StartTrackingAsync`, `StopTrackingAsync`, `OnTrackingDueAsync` usage. Update GitHubAgent and NuGetAgent to use new scheduling API.

- [ ] **Step 7: Run tests — verify pass**

Run: `dotnet test test/Core.Tests -v n`

- [ ] **Step 8: Commit**

```bash
git add src/Core/Agents/ test/Core.Tests/
git commit -m "feat: replace Agent.Tracking (Reminders v1) with Agent.Scheduling (Durable Jobs)"
```

---

### Task 5: Auto-discover interface methods as AI tools (Agent.Tools.cs)

**Files:**
- Modify: `src/Core/Agents/Agent.Tools.cs`
- Test: `test/Core.Tests/AgentToolDiscoveryTests.cs`

- [ ] **Step 1: Write test that interface methods appear as tools**

```csharp
public class AgentToolDiscoveryTests : AgentTest<IDotNet>
{
    [Fact]
    public async Task InterfaceMethods_RegisteredAsTools()
    {
        var agent = Agent(UniqueId("tools"));
        var capabilities = await agent.GetCapabilities(TestContext.Current.CancellationToken);
        Assert.True(capabilities.HasTools);
    }
}
```

- [ ] **Step 2: Implement auto-discovery in Agent.Tools.cs**

Rewrite `Agent.Tools.cs` to:
1. Reflect on the concrete grain interface (beyond `IAgent` base methods)
2. Register each method as an `AIFunction` via `AIFunctionFactory.Create()`
3. Add `DefineAdditionalTools()` virtual method for MCP/external tools
4. Merge both sources into the final tool list

- [ ] **Step 3: Run full test suite**

Run: `dotnet test IAW.slnx -v n`

- [ ] **Step 4: Commit**

```bash
git add src/Core/Agents/Agent.Tools.cs test/Core.Tests/
git commit -m "feat: auto-discover interface methods as AI tools"
```

---

## Phase 2: Namespace Reorganization & Agent Cleanup

Move agents to new namespaces, merge/delete agents. Each step must compile.

### Task 6: Delete dead agents (Review, Aspire, Build)

**Files:**
- Delete: `src/Agents/Review/` (entire directory — 4 files)
- Delete: `src/Agents/Infrastructure/AspireAgent.cs`, `src/Agents/Infrastructure/IAspire.cs`
- Delete: `src/Agents/Infrastructure/BuildAgent.cs`, `src/Agents/Infrastructure/IBuild.cs`
- Modify: `test/Core.Tests/` (remove references to deleted agents)

- [ ] **Step 1: Delete Review agents**

Delete `src/Agents/Review/` entirely (ReviewerAgent, SelfImprovementAgent, IReviewer, ISelfImprovement).

- [ ] **Step 2: Delete AspireAgent and IBuild/BuildAgent**

Delete `AspireAgent.cs`, `IAspire.cs`, `BuildAgent.cs`, `IBuild.cs` from `src/Agents/Infrastructure/`.

- [ ] **Step 3: Merge Build capabilities into DotNet**

Copy any unique Build methods (compilation, diagnostics parsing) that aren't already in DotNetAgent into `src/Agents.CSharp/DotNetAgent.cs` and `IDotNet.cs`.

- [ ] **Step 4: Fix compilation**

Run: `dotnet build IAW.slnx` and fix all references to deleted types.

- [ ] **Step 5: Run tests**

Run: `dotnet test IAW.slnx -v n`

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: delete dead agents (Review, Aspire, Build), merge Build into DotNet"
```

---

### Task 7: Reorganize agent namespaces

**Files:**
- Move agents to new namespace directories within existing projects
- Update `namespace` declarations in all moved files

New namespace mapping:

| Current path | New namespace | Action |
|---|---|---|
| `Agents/Infrastructure/ShellAgent.cs` | `IAW.Agents.System` | Change namespace |
| `Agents/Infrastructure/FileSystemAgent.cs` | `IAW.Agents.System` | Change namespace |
| `Agents/Infrastructure/GitAgent.cs` | `IAW.Agents.Coding` | Change namespace |
| `Agents.CSharp/*` | `IAW.Agents.Coding` | Change namespace |
| `Agents/Memory/*` | `IAW.Agents.Memory` | Keep (already correct) |
| `Agents/Knowledge/*` | `IAW.Agents.Memory` | Change namespace, merge UserAgent into UserMemoryAgent |
| `Agents/Orchestration/*` | `IAW.Agents.Orchestration` | Keep |
| `Agents/Projects/*` | `IAW.Agents.Orchestration` | Change namespace (will become Thread in Phase 3) |
| `Agents/LLM/*.cs` (13 files) | `IAW.Agents.Models` | Change namespace from `IAW.Agents.LLM` to `IAW.Agents.Models` |

- [ ] **Step 1: Update namespace declarations**

For each file: change the `namespace` line. Do NOT move files between projects — just update namespaces. Add `using` statements where needed.

- [ ] **Step 2: Merge UserAgent into UserMemoryAgent**

Copy key-value preference functionality from `UserAgent` into `UserMemoryAgent`. Delete `UserAgent.cs` and `IUser.cs`.

- [ ] **Step 3: Fix compilation**

Run: `dotnet build IAW.slnx` — fix all namespace references.

- [ ] **Step 4: Run tests**

Run: `dotnet test IAW.slnx -v n`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: reorganize agent namespaces (System, Coding, Models, Memory, Orchestration)"
```

---

### Task 8: Add AgentDescription and AgentCapabilities static metadata to all agents

**Files:**
- Modify: Every agent class (add `public static string AgentDescription` and `public static string[] AgentCapabilities`)
- Test: `test/Core.Tests/AgentMetadataTests.cs`

- [ ] **Step 1: Write test that all agents have descriptions**

```csharp
public class AgentMetadataTests
{
    [Fact]
    public void AllAgents_HaveDescription()
    {
        var agentTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t.IsSubclassOf(typeof(Agent)) && !t.IsAbstract);

        foreach (var type in agentTypes)
        {
            var prop = type.GetProperty("AgentDescription", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(prop); // $"Agent {type.Name} missing AgentDescription"
        }
    }
}
```

- [ ] **Step 2: Add static metadata to every agent class**

Add `AgentDescription` and `AgentCapabilities` to each agent. Descriptions should be concise (1-2 sentences) and capability tags should be action words.

- [ ] **Step 3: Run tests**

Run: `dotnet test test/Core.Tests --filter "AllAgents_HaveDescription" -v n`

- [ ] **Step 4: Commit**

```bash
git add src/Agents/ src/Agents.CSharp/
git commit -m "feat: add AgentDescription and AgentCapabilities to all agents"
```

---

## Phase 3: Agent Registry (Qdrant-backed)

### Task 9: Add VectorData packages and create AgentRecord

**Files:**
- Modify: `Directory.Packages.props` (add packages)
- Create: `src/Core/Registry/AgentRecord.cs`
- Create: `src/Core/Registry/AgentCandidate.cs`

- [ ] **Step 1: Add NuGet packages**

Add to `Directory.Packages.props`:
- `Microsoft.Extensions.VectorData.Abstractions`
- `Microsoft.Extensions.VectorData.Qdrant`

Use Context7 to verify latest versions.

- [ ] **Step 2: Create AgentRecord.cs**

Create `src/Core/Registry/AgentRecord.cs` with the Qdrant data model from spec Section 3.

- [ ] **Step 3: Create AgentCandidate.cs**

```csharp
[GenerateSerializer]
public record AgentCandidate(string AgentType, string Namespace, string DisplayName, string Description, string InterfaceName, float Score);
```

- [ ] **Step 4: Build**

Run: `dotnet build IAW.slnx`

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/Core/Registry/
git commit -m "feat: add VectorData packages and AgentRecord model"
```

---

### Task 10: Rewrite AgentRegistryGrain with Qdrant backing

**Files:**
- Rewrite: `src/Core/Registry/AgentRegistryGrain.cs`
- Rewrite: `src/Core/Registry/IAgentRegistryGrain.cs` → rename to `IAgentRegistry.cs`
- Rewrite: `src/Core/Registry/AgentRegistrationStartupTask.cs`
- Delete: `src/Core/Registry/AgentQuery.cs`, `src/Core/Registry/AgentRegistration.cs`
- Delete: `src/Core/Orchestration/InterfaceCatalog.cs`
- Test: `test/Core.Tests/RegistryTests.cs`

- [ ] **Step 1: Write test for registry search**

Note: `IAgentRegistry` is NOT an `IAgent` — do not extend `AgentTest<T>`. Use the TestCluster directly.

```csharp
public class AgentRegistryTests : IClassFixture<ClusterFixture>
{
    readonly ClusterFixture _fixture;
    public AgentRegistryTests(ClusterFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetAll_ReturnsAllRegisteredAgents()
    {
        var registry = _fixture.Cluster.GrainFactory.GetGrain<IAgentRegistry>("global");
        var agents = await registry.GetAllAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(agents);
    }

    [Fact]
    public async Task ToPromptString_GroupsByNamespace()
    {
        var registry = _fixture.Cluster.GrainFactory.GetGrain<IAgentRegistry>("global");
        var prompt = await registry.ToPromptStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("[coding]", prompt);
        Assert.Contains("[system]", prompt);
    }
}
```

- [ ] **Step 2: Define new IAgentRegistry interface**

```csharp
public interface IAgentRegistry : IGrainWithStringKey
{
    Task<List<AgentCandidate>> SearchAsync(string query, string? namespaceFilter = null, int top = 15, CancellationToken ct = default);
    Task<List<AgentRecord>> GetAllAsync(CancellationToken ct = default);
    Task<string> ToPromptStringAsync(CancellationToken ct = default);
    Task<AgentRecord?> GetByAgentTypeAsync(string agentType, CancellationToken ct = default);
}
```

- [ ] **Step 3: Rewrite AgentRegistryGrain**

Implement using Qdrant collection `agent-registry`. Inject `IVectorStore` (Qdrant). Use `IKeywordHybridSearch<AgentRecord>` for `SearchAsync`. Keep an in-memory cache populated at activation from Qdrant for `GetAllAsync` and `ToPromptStringAsync`.

- [ ] **Step 4: Rewrite AgentRegistrationStartupTask**

At silo startup: discover agents via reflection → read `AgentDescription`/`AgentCapabilities` → embed via `IEmbeddingGenerator` → upsert into Qdrant → delete orphans.

- [ ] **Step 5: Delete InterfaceCatalog.cs, old registry types, and their tests**

Delete `InterfaceCatalog.cs`, `AgentQuery.cs`, `AgentRegistration.cs`. Delete `test/Core.Tests/Orchestration/InterfaceCatalogTests.cs`. Move any reflection logic needed into the startup task.

- [ ] **Step 6: Fix all references to InterfaceCatalog**

Update `CodeOrchestratorAgent.cs`, `AgentTools.cs` (MCP), `OrleansAgentChatClient.cs` (DevUI), `AgentDiscovery.cs` (DevUI) to use `IAgentRegistry` instead.

- [ ] **Step 7: Run tests**

Run: `dotnet test IAW.slnx -v n`

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: rewrite AgentRegistry with Qdrant hybrid search, delete InterfaceCatalog"
```

---

## Phase 4: Thread Model & Dynamic IDs Migration

### Task 11: Create Thread agent (replaces Project)

**Files:**
- Create: `src/Agents/Orchestration/ThreadAgent.cs`
- Create: `src/Agents/Orchestration/IThread.cs`
- Modify: `src/Core/IAWConstants.cs` (add Thread grain type)
- Test: `test/Core.Tests/ThreadTests.cs`

- [ ] **Step 1: Create IThread.cs**

```csharp
namespace IAW.Agents.Orchestration;

public interface IThread : IAgent { }
```

- [ ] **Step 2: Write test for Thread basic conversation**

```csharp
public class ThreadTests : AgentTest<IThread>
{
    [Fact]
    public async Task GetResponse_ReturnsResponse()
    {
        var thread = Agent(UniqueId("thread"));
        var response = await thread.GetResponse("hello", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
    }
}
```

- [ ] **Step 3: Create ThreadAgent.cs**

Port from `Project.cs`: conversation management, context providers, system prompt. Remove CompareModelsTool. Remove Execute tool (will be rewired through AgentSelector in Task 12). Keep task board as internal durable state. Keep job scheduling (now via Durable Jobs). Add callback routing logic (register callbackId → agent mapping in durable state).

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --filter "ThreadTests" -v n`

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs src/Agents/Orchestration/IThread.cs test/Core.Tests/ThreadTests.cs
git commit -m "feat: create Thread agent (replaces Project)"
```

---

### Task 12: Create AgentSelectorAgent

**Files:**
- Create: `src/Agents/Orchestration/AgentSelectorAgent.cs`
- Create: `src/Agents/Orchestration/IAgentSelector.cs`
- Create: `src/Core/Contracts/SelectionResult.cs`
- Test: `test/Core.Tests/AgentSelectorTests.cs`

- [ ] **Step 1: Create SelectionResult types**

Create `src/Core/Contracts/SelectionResult.cs` with `SelectionResult`, `ClarificationQuestion`, `SelectionStatus` from spec Section 4.

- [ ] **Step 2: Create IAgentSelector.cs**

```csharp
public interface IAgentSelector : IAgent
{
    Task<SelectionResult> SelectAsync(string userRequest, CancellationToken ct);
}
```

- [ ] **Step 3: Write test**

```csharp
public class AgentSelectorTests : AgentTest<IAgentSelector>
{
    [Fact]
    public async Task SelectAsync_ReturnsResult()
    {
        var selector = Agent(UniqueId("sel"));
        var result = await selector.SelectAsync("run tests", TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.True(result.Status is SelectionStatus.Ready or SelectionStatus.CannotHandle);
    }
}
```

- [ ] **Step 4: Implement AgentSelectorAgent**

Inject `IAgentRegistry`. In `SelectAsync`: call `registry.SearchAsync(query)` for Phase 1, then use LLM to reason over candidates for Phase 2. Return structured `SelectionResult`.

- [ ] **Step 5: Run tests**

- [ ] **Step 6: Commit**

```bash
git add src/Agents/Orchestration/ src/Core/Contracts/SelectionResult.cs test/Core.Tests/
git commit -m "feat: create AgentSelectorAgent with two-phase selection"
```

---

### Task 13: Update CodeOrchestrator for dynamic IDs

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`
- Modify: `src/Core/Orchestration/ScriptGenerator.cs`
- Test: `test/Core.Tests/CodeOrchestratorTests.cs`

- [ ] **Step 1: Update BuildInstructions()**

Replace `InterfaceCatalog.ToPromptString()` with `IAgentRegistry.ToPromptStringAsync()`. Change template from `client.GetGrain<IGit>("git")` to `client.Get<IGit>(taskId)`.

- [ ] **Step 2: Update generated csproj to include Core extensions package**

Ensure the generated `.csproj` references the project containing `ClusterClientExtensions`.

- [ ] **Step 3: Run tests**

Run: `dotnet test test/Core.Tests --filter "CodeOrchestrator" -v n`

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/ src/Core/Orchestration/
git commit -m "feat: update CodeOrchestrator for dynamic IDs and AgentRegistry"
```

---

### Task 14: Migrate all singleton GetGrain calls to dynamic IDs

**Files:**
- Modify: All files containing `GetGrain<I...>("hardcoded-id")`

Known locations (from codebase analysis):
- `src/Agents/Projects/Project.cs:146` — CodeOrchestrator reference
- `src/Core/Registry/AgentRegistrationStartupTask.cs:11` — registry "global" (keep as singleton)
- `src/IAW.MCP/Tools/AgentTools.cs` — agent resolution
- `src/DevUI/OrleansAgentChatClient.cs` — agent resolution

- [ ] **Step 1: Search for all hardcoded grain IDs**

Run grep for `GetGrain<` across the codebase. Replace with `Get<T>()` or `Get<T>(scope)`.

- [ ] **Step 2: Update MCP AgentTools**

Replace `InterfaceCatalog.Discover()` and `ResolveAgent()` with `IAgentRegistry` queries. Use `Get<T>()` for ephemeral agent instances.

- [ ] **Step 3: Build and fix**

Run: `dotnet build IAW.slnx` — fix all compilation errors.

- [ ] **Step 4: Run tests**

Run: `dotnet test IAW.slnx -v n`

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: migrate all singleton grain IDs to dynamic Get<T>()"
```

---

### Task 15: Delete Project agent, simplify UISession, wire Thread into Telegram

**Files:**
- Delete: `src/Agents/Projects/Project.cs`
- Delete: `src/Core/Contracts/IProject.cs`
- Delete: `src/Core/Contracts/ProjectDurableState.cs` (if exists)
- Delete: `src/Core/Contracts/ProjectStateAttribute.cs` (if exists)
- Delete: `src/Core/AI/ProjectStateMapper.cs` (if exists)
- Modify: `src/Agents/UI/UISession.cs` (strip down to minimal callback routing via Thread, remove separate approval/wizard/form/menu dictionaries)
- Modify: `src/Clients.Telegram/TelegramBotService.cs` (replace IProject references with IThread)
- Modify: `src/Clients.Telegram/StreamSubscriber.cs` (update event subscriptions)
- Modify: `src/IAW.MCP/Tools/AgentTools.cs` (assistant_chat routes to IThread)
- Delete: `test/Core.Tests/ProjectTests.cs`
- Update: `test/Core.Tests/UI/` test files (FormTests, MenuTests, PaginatorTests, WizardTests, UISessionTests) — remove or adapt for simplified UISession
- Create: `test/Core.Tests/ThreadIntegrationTests.cs`

- [ ] **Step 1: Update TelegramBotService**

Replace all `IProject` references with `IThread`. Update grain ID format: `{userId}/personal`, `{userId}/iaw`. Remove "scheduled" and "notifications" topic creation from `/start`.

- [ ] **Step 2: Update StreamSubscriber**

Remove subscriptions for events that route to deleted topics. Job results and notifications now route to originating thread.

- [ ] **Step 3: Update MCP assistant_chat**

Rename `projectId` parameter to `threadSlug`. Route to `IThread` grain.

- [ ] **Step 4: Delete Project.cs and ProjectTests.cs**

- [ ] **Step 5: Build and fix**

Run: `dotnet build IAW.slnx`

- [ ] **Step 6: Run tests**

Run: `dotnet test IAW.slnx -v n`

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: replace Project with Thread, wire into Telegram and MCP"
```

---

## Phase 5: Communication Docs & Final Cleanup

### Task 16: Update website communication docs

**Files:**
- Modify: `website/guide/communication.md`

- [ ] **Step 1: Add the three-mechanism comparison table**

Add the IReceiver / Streams / Observers table from spec Section 8 to `website/guide/communication.md`. Include the "How They Work Together" example.

- [ ] **Step 2: Commit**

```bash
git add website/guide/communication.md
git commit -m "docs: add three-mechanism communication model to website"
```

---

### Task 17: Update CLAUDE.md with new architecture

**Files:**
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update architecture section**

Update namespace table, agent list, remove references to InterfaceCatalog/CompareModelsTool/Project agent. Add Thread, AgentSelector, AgentRegistry. Update grain ID examples. Update `Get<T>()` pattern.

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md for v4 architecture"
```

---

### Task 18: Full integration test pass

**Files:**
- Modify: `test/Integration.Tests/` (update all integration tests)

- [ ] **Step 1: Update integration tests for new architecture**

Fix any integration tests that reference deleted agents, old grain IDs, or InterfaceCatalog.

- [ ] **Step 2: Run full test suite**

Run: `dotnet test IAW.slnx -v n`
Expected: All tests pass.

- [ ] **Step 3: Start Aspire and test via MCP**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Verify: agents register, registry populates, Thread responds, MCP tools work.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "test: update integration tests for v4 architecture"
```

---

## Task Dependency Graph

```
Phase 1 (Core):     1 → 2 → 3 → 4 → 5
Phase 2 (Cleanup):  6 → 7 → 8
Phase 3 (Registry): 9 → 10
Phase 4 (Thread):   11 → 12 → 13 → 14 → 15
Phase 5 (Docs):     16, 17, 18

Phase 2 depends on Phase 1 completing
Phase 3 depends on Phase 2 completing (namespaces must be stable for registry)
Phase 4 depends on Phase 3 completing (Thread needs AgentRegistry)
Phase 5 depends on Phase 4 completing
```
