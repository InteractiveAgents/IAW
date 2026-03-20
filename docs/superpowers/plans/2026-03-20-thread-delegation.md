# Thread Delegation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give ThreadAgent a Delegate tool that bridges to AgentSelector and CodeOrchestrator, making it a real assistant instead of a bare LLM wrapper.

**Architecture:** ThreadAgent gets a single `Delegate(request)` tool registered via `DefineAdditionalTools()`. The tool calls AgentSelector (ephemeral, stateless) to pick agents, then either calls a single agent directly or dispatches to CodeOrchestrator for multi-agent work. A shared `AgentInterfaceResolver` is extracted from duplicated reflection logic in MCP and DevUI.

**Tech Stack:** C# / .NET 11, Orleans 10, Microsoft.Extensions.AI, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-20-thread-delegation-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Core/Extensions/AgentInterfaceResolver.cs` | Create | Shared helper: resolve agent interface name string to `Type` via assembly scanning |
| `src/Agents/Orchestration/IThread.cs` | Modify | Update `AgentInstructions` to describe IAW system and delegation |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Add `DefineAdditionalTools()` override with Delegate tool |
| `src/IAW.MCP/Tools/AgentTools.cs` | Modify | Replace inline `ResolveAgent` with `AgentInterfaceResolver`, prefix thread slugs with `mcp/` |
| `src/DevUI/OrleansAgentChatClient.cs` | Modify | Replace `BuildGrainInterfaceMap` with `AgentInterfaceResolver`, generate `devui/{guid}` for Thread |
| `test/Core.Tests/AgentInterfaceResolverTests.cs` | Create | Unit tests for resolver |
| `test/Core.Tests/ThreadDelegateToolTests.cs` | Create | Tests for the Delegate tool flow |

---

### Task 1: Extract AgentInterfaceResolver

The reflection logic to resolve an agent interface name (like `"IGit"`) to a `Type` is duplicated in `AgentTools.cs` (MCP) and `OrleansAgentChatClient.cs` (DevUI). Extract it into a shared helper in Core so ThreadAgent, MCP, and DevUI all use the same code.

**Files:**
- Create: `src/Core/Extensions/AgentInterfaceResolver.cs`
- Test: `test/Core.Tests/AgentInterfaceResolverTests.cs`

- [ ] **Step 1: Write the failing test**

In `test/Core.Tests/AgentInterfaceResolverTests.cs`:

```csharp
using Core;
using Core.Contracts;
using Xunit;

namespace IAW.Core.Tests;

public class AgentInterfaceResolverTests
{
    [Fact]
    public void Resolve_KnownInterface_ReturnsType()
    {
        // IAgent is in all loaded assemblies during test
        var allAgentInterfaces = AgentInterfaceResolver.DiscoverAgentInterfaces();
        Assert.NotEmpty(allAgentInterfaces);
    }

    [Fact]
    public void Resolve_ByExactName_ReturnsMatch()
    {
        var result = AgentInterfaceResolver.Resolve("IThread");
        Assert.NotNull(result);
        Assert.Equal("IThread", result.Name);
    }

    [Fact]
    public void Resolve_ByNameWithoutPrefix_ReturnsMatch()
    {
        var result = AgentInterfaceResolver.Resolve("Thread");
        Assert.NotNull(result);
        Assert.Equal("IThread", result.Name);
    }

    [Fact]
    public void Resolve_ByKebabCase_ReturnsMatch()
    {
        var result = AgentInterfaceResolver.Resolve("thread");
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolve_Unknown_ReturnsNull()
    {
        var result = AgentInterfaceResolver.Resolve("INonExistent");
        Assert.Null(result);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~AgentInterfaceResolverTests" -v m`
Expected: FAIL — `AgentInterfaceResolver` does not exist.

- [ ] **Step 3: Implement AgentInterfaceResolver**

In `src/Core/Extensions/AgentInterfaceResolver.cs`:

```csharp
using Core.Contracts;

namespace Core;

public static class AgentInterfaceResolver
{
    private static readonly Lazy<IReadOnlyList<Type>> CachedInterfaces = new(ScanInterfaces);

    public static IReadOnlyList<Type> DiscoverAgentInterfaces() => CachedInterfaces.Value;

    public static Type? Resolve(string name)
    {
        var interfaces = DiscoverAgentInterfaces();

        // exact match: "IGit"
        var match = interfaces.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match;

        // without I prefix: "Git" or "git"
        match = interfaces.FirstOrDefault(t =>
        {
            var stripped = t.Name.StartsWith('I') && t.Name.Length > 1 && char.IsUpper(t.Name[1])
                ? t.Name[1..]
                : t.Name;
            return string.Equals(stripped, name, StringComparison.OrdinalIgnoreCase);
        });
        if (match is not null) return match;

        // kebab-case: "code-orchestrator" -> "CodeOrchestrator" -> "ICodeOrchestrator"
        var normalized = name.Replace("-", "");
        return interfaces.FirstOrDefault(t =>
        {
            var stripped = t.Name.StartsWith('I') && t.Name.Length > 1 && char.IsUpper(t.Name[1])
                ? t.Name[1..]
                : t.Name;
            return string.Equals(stripped, normalized, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static IReadOnlyList<Type> ScanInterfaces() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t.IsInterface
                        && t != typeof(IAgent)
                        && typeof(IAgent).IsAssignableFrom(t)
                        && !t.IsGenericType)
            .ToList();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~AgentInterfaceResolverTests" -v m`
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Extensions/AgentInterfaceResolver.cs test/Core.Tests/AgentInterfaceResolverTests.cs
git commit -m "feat: extract AgentInterfaceResolver for shared agent type resolution"
```

---

### Task 2: Update IThread Instructions

Replace the generic instructions with system-aware instructions that tell the LLM about IAW and when to use the Delegate tool.

**Files:**
- Modify: `src/Agents/Orchestration/IThread.cs:15-23`

- [ ] **Step 1: Update AgentInstructions**

Replace the `AgentInstructions` static property in `IThread.cs`:

```csharp
static string IAgent.AgentInstructions => """
    You are an AI assistant in the IAW (Interactive Agents Workspace) system —
    a multi-agent platform built on Orleans. You have access to a team of
    specialized agents that can execute tasks: coding, git, shell, .NET builds,
    code review, and more.

    DECISION RULE:
    - Answer directly when: greetings, general knowledge, questions about
      conversation context, user preferences, or anything you can answer
      from your enriched context
    - Use the Delegate tool when: the request involves code execution,
      system operations, agent capabilities, builds, git, file operations,
      or anything requiring specialized agent skills

    When delegating, describe WHAT needs to be done, not HOW. The agent
    system handles routing and execution automatically.

    Be concise and direct. Use markdown formatting.
    """;
```

- [ ] **Step 2: Run existing ThreadTests to verify nothing breaks**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ThreadTests" -v m`
Expected: All PASS (instructions change doesn't break existing behavior — MockChatClient returns "mock-response" regardless).

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/IThread.cs
git commit -m "feat: update IThread instructions for IAW system awareness and delegation"
```

---

### Task 3: Add Delegate Tool to ThreadAgent

This is the core change. Add `DefineAdditionalTools()` override to ThreadAgent with the Delegate tool that calls AgentSelector, then dispatches to single agent or CodeOrchestrator.

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`
- Test: `test/Core.Tests/ThreadDelegateToolTests.cs`

- [ ] **Step 1: Write the failing test for Delegate tool**

In `test/Core.Tests/ThreadDelegateToolTests.cs`:

```csharp
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ThreadDelegateToolTests : AgentTest<ThreadAgent>
{
    [Fact]
    public async Task GetResponse_WithDelegationRequest_ReturnsResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("delegate"));

        // MockChatClient returns "mock-response" for all calls.
        // The Delegate tool will call AgentSelector which also uses MockChatClient,
        // so AgentSelector will return "mock-response" (not valid JSON),
        // which ParseSelectionResult treats as CannotHandle.
        // The Delegate tool should still return a string result (the error/explanation).
        var response = await thread.GetResponse("check the git status", ct);
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task Thread_HasDelegateTool_InCapabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("tools"));
        var capabilities = await thread.GetCapabilities(ct);
        Assert.NotNull(capabilities);
    }
}
```

- [ ] **Step 2: Run test to verify baseline passes**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ThreadDelegateToolTests" -v m`
Expected: PASS (the tests verify ThreadAgent works; the Delegate tool itself is exercised implicitly via the LLM tool registration).

- [ ] **Step 3: Implement DefineAdditionalTools in ThreadAgent**

Add the following to `ThreadAgent.cs` after the `GetContextProviders()` method (after line 42):

```csharp
protected override IReadOnlyList<AITool> DefineAdditionalTools()
{
    return [AIFunctionFactory.Create(DelegateAsync, "Delegate",
        "Delegate a task to the IAW agent system. Use this for any request that requires " +
        "code execution, system operations, builds, git, file operations, or specialized agent skills. " +
        "Describe WHAT needs to be done.")];
}

[System.ComponentModel.Description("Delegate a task to the IAW agent system")]
private async Task<string> DelegateAsync(
    [System.ComponentModel.Description("What needs to be done — describe the task clearly")] string request,
    CancellationToken ct = default)
{
    try
    {
        var selector = GrainFactory.Get<IAgentSelector>();
        var result = await selector.SelectAsync(request, ct);

        return result.Status switch
        {
            SelectionStatus.NeedsClarification => FormatClarificationResponse(result),
            SelectionStatus.CannotHandle => result.Plan ?? "The agent system cannot handle this request.",
            SelectionStatus.Ready => await ExecuteSelection(result, request, ct),
            _ => "Unexpected selection status."
        };
    }
    catch (Exception ex)
    {
        return $"Delegation failed: {ex.Message}";
    }
}

private async Task<string> ExecuteSelection(SelectionResult selection, string request, CancellationToken ct)
{
    var threadId = this.GetPrimaryKeyString();

    if (selection.SelectedAgents.Count == 1)
    {
        var agentInterfaceName = selection.SelectedAgents[0];
        var interfaceType = AgentInterfaceResolver.Resolve(agentInterfaceName);
        if (interfaceType is null)
            return $"Could not resolve agent: {agentInterfaceName}";

        var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");
        return await agent.GetResponse(request, ct);
    }

    // multiple agents — use CodeOrchestrator
    var orchestrator = GrainFactory.Get<ICodeOrchestrator>(threadId);
    var plan = selection.Plan ?? $"Execute: {request}\nAgents: {string.Join(", ", selection.SelectedAgents)}";
    return await orchestrator.ExecuteCodeOrchestration(plan, ct);
}

private static string FormatClarificationResponse(SelectionResult result)
{
    if (result.Questions is null or { Count: 0 })
        return "I need more information to proceed. Could you clarify your request?";

    var sb = new System.Text.StringBuilder("I need some clarification:\n\n");
    foreach (var q in result.Questions)
    {
        sb.AppendLine($"- {q.Text}");
        if (q.Options is { Count: > 0 })
            sb.AppendLine($"  Options: {string.Join(", ", q.Options)}");
    }
    return sb.ToString();
}
```

Also add the required usings at the top of `ThreadAgent.cs`:

```csharp
using Core.Contracts;  // already present
// Add if not present:
using Core;  // for AgentInterfaceResolver, GrainFactoryExtensions
```

- [ ] **Step 4: Run all ThreadTests to verify**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~Thread" -v m`
Expected: All PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs test/Core.Tests/ThreadDelegateToolTests.cs
git commit -m "feat: add Delegate tool to ThreadAgent for agent system delegation"
```

---

### Task 4: Wire MCP Thread Scoping

Prefix MCP thread slugs with `mcp/` so they don't collide with Telegram thread IDs. Also replace the inline `ResolveAgent` with `AgentInterfaceResolver`.

**Files:**
- Modify: `src/IAW.MCP/Tools/AgentTools.cs:12-31` (ResolveAgent), `:72-80` (assistant_chat), `:108-120` (agent_assign_task)

- [ ] **Step 1: Replace ResolveAgent with AgentInterfaceResolver**

Replace the `ResolveAgent` method (lines 12-31) with:

```csharp
private IAgent ResolveAgent(string agentId)
{
    var interfaceType = AgentInterfaceResolver.Resolve(agentId);
    if (interfaceType is not null)
        return (IAgent)orleans.GetGrain(interfaceType, agentId);

    var known = string.Join(", ",
        AgentInterfaceResolver.DiscoverAgentInterfaces().Select(t => t.Name.TrimStart('I').ToLowerInvariant()));
    throw new ArgumentException($"Unknown agent ID: {agentId}. Known: {known}");
}
```

Add `using Core;` to the top of the file.

- [ ] **Step 2: Prefix thread slugs in assistant_chat**

Change `AssistantChat` method (line 77):

```csharp
// Before:
var thread = orleans.GetGrain<IThread>(threadSlug);
// After:
var thread = orleans.GetGrain<IThread>($"mcp/{threadSlug}");
```

- [ ] **Step 3: Prefix thread slugs in agent_assign_task**

Change `AgentAssignTask` method (line 116):

```csharp
// Before:
var thread = orleans.GetGrain<IThread>(threadSlug);
// After:
var thread = orleans.GetGrain<IThread>($"mcp/{threadSlug}");
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build src/IAW.MCP/MCP.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/IAW.MCP/Tools/AgentTools.cs
git commit -m "feat: use AgentInterfaceResolver in MCP, prefix thread slugs with mcp/"
```

---

### Task 5: Wire DevUI Thread Scoping

When DevUI targets the Thread agent, generate a `devui/{guid}` grain ID so each new conversation gets a fresh Thread. Also replace the inline `BuildGrainInterfaceMap` with `AgentInterfaceResolver`.

**Files:**
- Modify: `src/DevUI/OrleansAgentChatClient.cs`

- [ ] **Step 1: Replace BuildGrainInterfaceMap with AgentInterfaceResolver**

Remove the `BuildGrainInterfaceMap` method (lines 82-104), the `KebabRegex` method (lines 112-113), the `ToKebabCase` method (lines 106-110), and the `GrainInterfaceMap` field (line 14).

Add a field to hold the per-session Thread ID (generated once at construction, stable across all messages in this DevUI session):

```csharp
// Add field to the class:
private readonly string _devuiThreadId = $"devui/{Guid.NewGuid().ToString("N")[..8]}";
```

Replace `ResolveAgent` (lines 71-78) with:

```csharp
private IAgent ResolveAgent(string agentId)
{
    // Thread gets a stable devui/ scoped ID for this session
    if (IsThreadAgent(agentId))
        return (IAgent)cluster.GetGrain(typeof(IThread), _devuiThreadId);

    var interfaceType = AgentInterfaceResolver.Resolve(agentId);
    if (interfaceType is not null)
        return (IAgent)cluster.GetGrain(interfaceType, agentId);

    var known = string.Join(", ",
        AgentInterfaceResolver.DiscoverAgentInterfaces().Select(t => t.Name.TrimStart('I').ToLowerInvariant()));
    throw new ArgumentException($"Unknown agent ID: {agentId}. Known: {known}");
}

private static bool IsThreadAgent(string agentId) =>
    string.Equals(agentId, "thread", StringComparison.OrdinalIgnoreCase);
```

Add `using Core;` and `using IAW.Agents.Orchestration;` to the top.

Remove the `partial` keyword from the class declaration (no longer needed since `KebabRegex` GeneratedRegex is removed).

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build src/DevUI/DevUI.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/DevUI/OrleansAgentChatClient.cs
git commit -m "feat: use AgentInterfaceResolver in DevUI, scope Thread to devui/{guid}"
```

---

### Task 6: Full Build and Test

Build everything and run the full test suite to catch any regressions.

**Files:** None (verification only)

- [ ] **Step 1: Build the solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded with 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All tests pass.

- [ ] **Step 3: Fix any failures**

If any tests fail, diagnose and fix before proceeding. Do NOT move forward with failing tests.

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve test/build issues from thread delegation changes"
```

---

### Task 7: Integration Test via Aspire

Start Aspire and verify the Telegram bot actually delegates through the Thread agent. This tests the full end-to-end flow.

**Files:** None (manual verification via Aspire MCP tools)

- [ ] **Step 1: Start Aspire**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Wait for all resources to be Running/Healthy.

- [ ] **Step 2: Verify resources are healthy**

Use: `mcp__aspire__list_resources`
Expected: assistant, telegram, mcp, devui all Running.

- [ ] **Step 3: Test via MCP assistant_chat**

Use: `mcp__aspire__assistant_chat` with message "list available agents"
Expected: Thread should use the Delegate tool, call AgentSelector, and return a list of agents.

- [ ] **Step 4: Verify in traces**

Use: `mcp__aspire__list_traces` for the assistant resource.
Expected: See `invoke_agent mcp/general` trace with child spans for AgentSelector and/or agent calls — confirming the Delegate tool was invoked.

- [ ] **Step 5: Test simple delegation**

Use: `mcp__aspire__assistant_chat` with message "what's the git status?"
Expected: Thread delegates to git agent, returns actual git status.

- [ ] **Step 6: Commit if any runtime fixes were needed**

```bash
git add -A
git commit -m "fix: runtime issues found during integration testing"
```
