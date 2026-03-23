# Orchestration Fast Path & Token Efficiency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce token consumption by ~88% for simple tasks by letting Thread route directly to agents, and fix ShellAgent's missing timeout that blocks GUI apps forever.

**Architecture:** Thread gets a `SendToAgent(agentName, request)` tool so simple tasks go Thread → Agent (2 cheap LLM calls) instead of Thread → AgentSelector → CodeOrchestrator (3+ expensive calls). Infrastructure agents (DotNet, FileSystem, Git) switch to `[Llm<Fast>]` since they only need to pick which tool to call. CodeOrchestrator prompt is slimmed from ~8KB to ~3KB by filtering to selected agents only.

**Tech Stack:** Orleans grains, Microsoft.Extensions.AI, C# 13, xunit.v3

---

### Task 1: Fix ShellAgent.RunDotnetAsync missing timeout (critical bug)

**Files:**
- Modify: `src/Agents/Infrastructure/ShellAgent.cs:98-128`
- Test: `test/Core.Tests/` (run existing tests with Shell filter)

- [ ] **Step 1: Add timeout to RunDotnetAsync**

`ShellAgent.cs` — replace the `RunDotnetAsync` method (lines 98-128) to mirror `ExecuteAsync`'s timeout pattern:

```csharp
public async Task<CommandResult> RunDotnetAsync(
    string arguments, string? workingDirectory = null, CancellationToken ct = default)
{
    var effectiveDirectory = workingDirectory ?? GetWorkspacePath() ?? Directory.GetCurrentDirectory();
    var sw = Stopwatch.StartNew();

    var psi = new ProcessStartInfo("dotnet", arguments)
    {
        WorkingDirectory = effectiveDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    if (process is null)
    {
        sw.Stop();
        return new CommandResult(-1, "", "Failed to start dotnet process", sw.Elapsed);
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(120_000);

    try
    {
        var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);
        sw.Stop();

        var result = new CommandResult(process.ExitCode, output, error, sw.Elapsed);
        await RecordCommandExecution($"dotnet {arguments}", result, ct);
        return result;
    }
    catch (OperationCanceledException)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        sw.Stop();

        var result = new CommandResult(-1, "", "dotnet command timed out after 120s", sw.Elapsed);
        await RecordCommandExecution($"dotnet {arguments}", result, ct);
        return result;
    }
}
```

- [ ] **Step 2: Build and run tests**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests --filter "FullyQualifiedName~Shell" -v minimal`
Expected: All shell tests pass, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Infrastructure/ShellAgent.cs
git commit -m "fix: add 120s timeout to RunDotnetAsync — GUI apps no longer block forever"
```

---

### Task 2: Add `ResolveByDisplayName` to AgentInterfaceResolver

**Files:**
- Modify: `src/Core/Extensions/AgentInterfaceResolver.cs`
- Test: `test/Core.Tests/AgentInterfaceResolverTests.cs` (existing file — add new tests)

Thread's `SendToAgent` tool needs to resolve agents by their display name (e.g., "Shell", "DotNet", "Git"). The existing `Resolve()` matches by interface name ("IShell"). We need a companion that matches by `AgentDisplayName`.

- [ ] **Step 1: Write the test**

Find or create the test file. Add:

```csharp
[Fact]
public void ResolveByDisplayName_FindsShellAgent()
{
    var result = AgentInterfaceResolver.ResolveByDisplayName("Shell");
    Assert.NotNull(result);
    Assert.Equal("IShell", result!.Name);
}

[Fact]
public void ResolveByDisplayName_CaseInsensitive()
{
    var result = AgentInterfaceResolver.ResolveByDisplayName("dotnet");
    Assert.NotNull(result);
    Assert.Equal("IDotNet", result!.Name);
}

[Fact]
public void ResolveByDisplayName_ReturnsNull_ForUnknown()
{
    var result = AgentInterfaceResolver.ResolveByDisplayName("NonExistentAgent");
    Assert.Null(result);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests --filter "ResolveByDisplayName" -v minimal`
Expected: FAIL — method does not exist.

- [ ] **Step 3: Implement ResolveByDisplayName**

Add to `AgentInterfaceResolver.cs` after the existing `Resolve` method:

```csharp
public static Type? ResolveByDisplayName(string displayName)
{
    var interfaces = DiscoverAgentInterfaces();
    return interfaces.FirstOrDefault(t =>
    {
        var (name, _, _) = AgentInterfaceMetadata.ReadFrom(t);
        return string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase);
    });
}
```

This requires adding `using Core.Registry;` at the top of the file. `ReadFrom` returns a tuple `(string DisplayName, string Description, string[] Capabilities)`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Core.Tests --filter "ResolveByDisplayName" -v minimal`
Expected: 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Extensions/AgentInterfaceResolver.cs test/Core.Tests/
git commit -m "feat: add ResolveByDisplayName to AgentInterfaceResolver"
```

---

### Task 3: Add SendToAgent + Orchestrate tools to ThreadAgent

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs:49-66`
- Modify: `src/Agents/Orchestration/IThread.cs` (update instructions)
- Test: `test/Core.Tests/ThreadTests.cs`, `test/Core.Tests/ThreadDelegateToolTests.cs` (existing)

This is the core change. Replace the single `Delegate` tool with `SendToAgent` (direct agent routing) and `Orchestrate` (existing delegation pipeline).

- [ ] **Step 1: Update IThread instructions**

In `src/Agents/Orchestration/IThread.cs`, replace the `AgentInstructions`:

```csharp
static string IAgent.AgentInstructions => """
    You are an AI assistant in the IAW (Interactive Agents Workspace) system —
    a multi-agent platform built on Orleans with specialized agents.

    ROUTING RULES:
    - Answer directly: greetings, general knowledge, conversation context
    - SendToAgent for single-agent tasks:
      • "Shell" — run commands, dotnet CLI, scripts
      • "DotNet" — build projects, run tests, format code
      • "FileSystem" — read/write/list/search files
      • "Git" — status, commit, diff, log, revert
      • "Roslyn" — code analysis, type maps, error diagnostics
      • "GitHub" — PRs, issues, repository operations
    - Orchestrate for complex multi-step tasks that need coordination
      across multiple agents (scaffolding + building + testing,
      multi-file refactoring with analysis, code generation pipelines)

    PREFER SendToAgent over Orchestrate. Most tasks need just one agent.
    Pass the user's request naturally — the agent handles the details.
    ALWAYS preserve exact paths from the user's message.
    Be concise and direct. Use markdown formatting.
    """;
```

- [ ] **Step 2: Replace DefineAdditionalTools in ThreadAgent**

In `src/Agents/Orchestration/ThreadAgent.cs`, replace `DefineAdditionalTools` (lines 49-57):

```csharp
protected override IReadOnlyList<AITool> DefineAdditionalTools()
{
    return [
        AIFunctionFactory.Create(SendToAgentAsync, "SendToAgent",
            "Send a task to a specific agent by name. The agent handles it autonomously " +
            "with its own LLM and tools. Available agents: Shell, DotNet, FileSystem, Git, Roslyn, GitHub."),

        AIFunctionFactory.Create(OrchestrateAsync, "Orchestrate",
            "For complex multi-step tasks requiring coordination across multiple agents. " +
            "NOT needed for single build/run/read/git tasks — use SendToAgent instead.")
    ];
}
```

- [ ] **Step 3: Implement SendToAgentAsync**

Add this method to `ThreadAgent.cs` (above the existing `DelegateAsync`):

```csharp
private async Task<string> SendToAgentAsync(string agentName, string request, CancellationToken ct = default)
{
    logger.LogInformation("SendToAgent: {Agent} for: {Request}",
        agentName, request[..Math.Min(80, request.Length)]);

    var interfaceType = AgentInterfaceResolver.ResolveByDisplayName(agentName)
                     ?? AgentInterfaceResolver.Resolve(agentName);
    if (interfaceType is null)
        return $"Unknown agent: {agentName}. Available: Shell, DotNet, FileSystem, Git, Roslyn, GitHub.";

    var threadId = this.GetPrimaryKeyString();
    var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");

    try
    {
        return await agent.GetResponse(request, ct);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SendToAgent: {Agent} failed", agentName);
        return $"Agent {agentName} failed: {ex.Message}";
    }
}
```

- [ ] **Step 4: Rename DelegateAsync → OrchestrateAsync**

Rename the existing `DelegateAsync` method to `OrchestrateAsync`. Only change the method name — the body stays identical:

```csharp
private async Task<string> OrchestrateAsync(string request, CancellationToken ct = default)
{
    var taskId = $"dlg-{Guid.NewGuid().ToString("N")[..8]}";
    logger.LogInformation("Orchestrate: executing {TaskId} for: {Request}",
        taskId, request[..Math.Min(80, request.Length)]);

    return await ExecuteDelegation(taskId, request, ct);
}
```

- [ ] **Step 5: Build and run tests**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests --filter "FullyQualifiedName~Thread" -v minimal`
Expected: All existing Thread tests pass. The test cluster uses MockChatClient so tool routing won't be exercised, but grain activation and method resolution must work.

- [ ] **Step 6: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs src/Agents/Orchestration/IThread.cs
git commit -m "feat: add SendToAgent tool for direct agent routing, rename Delegate to Orchestrate"
```

---

### Task 4: Switch infrastructure agents to Fast tier model

**Files:**
- Modify: `src/Agents.CSharp/DotNet/DotNetAgent.cs:13-17` (add `[Llm<Fast>]`)
- Modify: `src/Agents/Infrastructure/FileSystemAgent.cs:10-13` (add `[Llm<Fast>]`)
- Modify: `src/Agents/Infrastructure/GitAgent.cs:11-14` (add `[Llm<Fast>]`)

These agents only need to pick which tool to call from their interface methods — no complex reasoning. Switching from default (Gpt54Mini / Balanced) to Fast tier (Gpt54Nano) cuts token cost significantly.

Note: use `[Llm<Fast>]` (tier-based) rather than a concrete model, so the AppHost controls which model backs the Fast tier. Currently `Gpt54Nano` via `.AsFast()`.

- [ ] **Step 1: Add Fast model to DotNetAgent**

In `src/Agents.CSharp/DotNet/DotNetAgent.cs`, the file already has `using Core.AI;` which contains the `Fast` type. Change the constructor to add the attribute:

```csharp
public partial class DotNetAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient,
    IHttpClientFactory httpClientFactory)
    : Agent<IDotNet>(durableState, chatClient), IDotNet
```

- [ ] **Step 2: Add Fast model to FileSystemAgent**

In `src/Agents/Infrastructure/FileSystemAgent.cs`, the file already has `using Core.AI;`. Change the constructor:

```csharp
public class FileSystemAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient)
    : Agent<IFileSystem>(durableState, chatClient), IFileSystem
```

- [ ] **Step 3: Add Fast model to GitAgent**

In `src/Agents/Infrastructure/GitAgent.cs`, the file already has `using Core.AI;`. Change the constructor:

```csharp
public class GitAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient)
    : Agent<IGit>(durableState, chatClient), IGit
```

- [ ] **Step 4: Build and run all tests**

Run: `dotnet build IAW.slnx && dotnet test test/Core.Tests -v minimal`
Expected: All tests pass. `AgentTest<T>` registers all model attribute mappers with MockChatClient, so `[Llm<Fast>]` resolves in tests.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/DotNet/DotNetAgent.cs src/Agents/Infrastructure/FileSystemAgent.cs src/Agents/Infrastructure/GitAgent.cs
git commit -m "perf: switch DotNet, FileSystem, Git agents to Fast tier model"
```

---

### Task 5: Filter LLM agents from AgentSelector candidates

**Files:**
- Modify: `src/Agents/Orchestration/AgentSelectorAgent.cs:22-34`

The registry returns LLM wrapper agents (IGpt54Mini, ISonnet46, etc.) as candidates. The CodeOrchestrator prompt wastes tokens saying "IGNORE LLM agents." Fix: filter them out before the LLM sees them.

- [ ] **Step 1: Filter candidates in SelectAsync**

In `AgentSelectorAgent.cs`, after the `candidates = await registry.SearchAsync(...)` call (around line 25), add a filter:

```csharp
candidates = await registry.SearchAsync(userRequest, ct: ct);
candidates = candidates
    .Where(c => !string.Equals(c.Namespace, "models", StringComparison.OrdinalIgnoreCase))
    .ToList();
```

This removes all agents from the `models` namespace (where `Gpt54MiniAgent`, `Sonnet46Agent`, etc. live). The registry stores namespace as the lowercase last segment of the .NET namespace, so `IAW.Agents.Models` → `"models"`.

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/AgentSelectorAgent.cs
git commit -m "perf: filter LLM wrapper agents from selector candidates"
```

---

### Task 6: Slim CodeOrchestrator prompt — filter catalog to selected agents

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`

Currently the orchestrator gets the FULL agent catalog (~4KB) on every call. When selected agents are known (e.g., `[IDotNet, IShell]`), only include those agents' API signatures in the prompt.

- [ ] **Step 1: Modify ExecuteCodeOrchestration to filter catalog**

Find the `ExecuteCodeOrchestration` method. Where it builds the instructions with `_cachedAgentCatalog`, replace with filtered catalog:

In the `BuildInstructions` method, the `agentCatalog` parameter currently gets the full registry string. Change the `ExecuteCodeOrchestration` method to pass a filtered catalog:

```csharp
// In ExecuteCodeOrchestration, before calling BuildInstructions:
var filteredCatalog = selectedAgents.Count > 0
    ? FilterCatalogToSelectedAgents(_cachedAgentCatalog, selectedAgents)
    : _cachedAgentCatalog;
_cachedInstructions = BuildInstructions(filteredCatalog, workspacePath, selectedAgents);
```

Add the filter method. The catalog format from `ToPromptStringAsync` is:
```
## namespace
- **IInterfaceName** — description [capabilities]
```

So we match agent interface names in bullet-point lines:

```csharp
static string FilterCatalogToSelectedAgents(string fullCatalog, IReadOnlyList<string> selectedAgents)
{
    if (selectedAgents.Count == 0 || string.IsNullOrEmpty(fullCatalog))
        return fullCatalog;

    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# Agent Catalog");
    sb.AppendLine();

    foreach (var line in fullCatalog.Split('\n'))
    {
        var trimmed = line.TrimStart();

        // keep namespace headers (## coding, ## system) — they're short
        if (trimmed.StartsWith("## "))
        {
            sb.AppendLine(line);
            continue;
        }

        // keep agent lines that match selected agents: "- **IDotNet** — ..."
        if (trimmed.StartsWith("- **"))
        {
            if (selectedAgents.Any(a => trimmed.Contains(a, StringComparison.OrdinalIgnoreCase)))
                sb.AppendLine(line);
            continue;
        }
    }

    var result = sb.ToString();
    return result.Length > 30 ? result : fullCatalog;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs
git commit -m "perf: filter orchestrator catalog to selected agents only"
```

---

### Task 7: End-to-end test via Aspire

**Files:** None (manual testing)

Restart Aspire and test the full flow via MCP or Telegram.

- [ ] **Step 1: Stop all Aspire resources**

Use Aspire MCP tools to stop assistant, mcp, devui, telegram. Or kill the AppHost process.

- [ ] **Step 2: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 3: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All tests pass (except known pre-existing CodeValidator failures).

- [ ] **Step 4: Start Aspire**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Wait for all resources to reach Running state.

- [ ] **Step 5: Test simple task via MCP**

Use `assistant_chat` MCP tool:
```
Build the project at D:\Demo\Calc and tell me if it succeeds
```

Expected behavior:
- Thread calls `SendToAgent("DotNet", "Build project at D:\Demo\Calc")`
- DotNet agent calls `BuildAsync` tool
- Result returns in <10 seconds
- NO AgentSelector or CodeOrchestrator traces in Aspire

- [ ] **Step 6: Test run task via MCP**

```
Run dotnet run in D:\Demo\Calc
```

Expected:
- Thread calls `SendToAgent("Shell", "Run dotnet run in D:\Demo\Calc")`
- Shell agent calls `RunDotnetAsync`
- Returns within 120s (timeout kills GUI app)
- NO CodeOrchestrator involvement

- [ ] **Step 7: Test complex task still uses Orchestrate**

```
Create a new console app in D:\Demo\Test, add a Calculator class with Add/Subtract methods, write unit tests, build and run them
```

Expected:
- Thread calls `Orchestrate(...)` — complex multi-step task
- AgentSelector picks agents
- CodeOrchestrator generates C# app
- Full pipeline works as before

- [ ] **Step 8: Compare traces**

Check Aspire traces:
- Simple tasks: 1-2 `gen_ai` spans (Thread + target agent), <5K tokens total
- Complex tasks: 3+ `gen_ai` spans (Thread + Selector + Orchestrator), should be less than before due to filtered catalog

- [ ] **Step 9: Commit any fixes**

If any issues found during testing, fix and commit.
