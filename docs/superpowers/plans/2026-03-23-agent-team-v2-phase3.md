# Agent Team v2 — Phase 3: Optimization & Polish

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce token waste through compact tool results, actionable error messages, and deterministic pre-filters that skip LLM calls for known error patterns.

**Architecture:** Three targeted optimizations: (1) SendToAgent returns actionable errors with agent capabilities when calls fail, (2) agent tool results are truncated to prevent flooding Thread's context window, (3) CodeOrchestrator's self-healing loop checks deterministic patterns before spending tokens on LLM diagnosis.

**Tech Stack:** Orleans grains, C# 13, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-23-agent-team-v2-design.md` (Phase 3 section)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Actionable errors in SendToAgent, compact result truncation |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | Modify | Non-LLM pre-filters for deterministic build errors |
| `src/Core/Agents/Agent.cs` | Modify | Compact GetResponse — truncate tool results before returning to caller |

---

### Task 1: Actionable error messages in SendToAgent

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`

When SendToAgent fails, the current error message is `"Agent {agentName} failed: {ex.Message}"`. This gives the LLM no information about what to try instead. Fix: include the agent's capabilities and suggest alternatives.

- [ ] **Step 1: Update SendToAgentAsync error handling**

In `ThreadAgent.cs`, find `SendToAgentAsync`. Replace the catch block:

Current:
```csharp
catch (Exception ex)
{
    logger.LogError(ex, "SendToAgent: {Agent} failed", agentName);
    return $"Agent {agentName} failed: {ex.Message}";
}
```

Replace with:
```csharp
catch (OperationCanceledException)
{
    return $"Agent {agentName} timed out. Try a simpler request or a different agent.";
}
catch (Exception ex)
{
    logger.LogError(ex, "SendToAgent: {Agent} failed", agentName);
    var suggestion = agentName switch
    {
        "DotNet" => "Try Shell agent for raw dotnet CLI commands, or check the project path.",
        "Shell" => "Check command syntax. For .NET operations, use DotNet agent instead.",
        "FileSystem" => "Check file path exists. Use absolute paths.",
        "Git" => "Check repository path. Ensure it's a valid git repo.",
        "Aspire" => "Aspire MCP may not be connected. Try again after restart.",
        "Roslyn" => "Check that the workspace is set and contains C# code.",
        _ => "Try a different agent or rephrase the request."
    };
    return $"Agent {agentName} failed: {ex.Message}\nSuggestion: {suggestion}";
}
```

- [ ] **Step 2: Add result truncation to SendToAgentAsync**

Large agent responses (e.g., full build output, large file contents) flood Thread's context window. Add truncation BEFORE the return. Find the `return await agent.GetResponse(request, ct);` line and replace with:

```csharp
var result = await agent.GetResponse(request, ct);
return result.Length > 4000
    ? result[..4000] + "\n...(truncated, full output available in agent state)"
    : result;
```

- [ ] **Step 3: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests --filter "FullyQualifiedName~Thread" -v minimal`
Expected: 0 errors, all Thread tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs
git commit -m "perf: actionable error messages and result truncation in SendToAgent"
```

---

### Task 2: Non-LLM pre-filters for CodeOrchestrator self-healing

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`

The CodeOrchestrator's build-retry loop currently sends ALL build errors to the Opus LLM for diagnosis. Many errors have deterministic fixes that don't need an LLM call:
- Missing namespace → add the using
- File not found → path is wrong, skip
- Timeout → retry without changes

- [ ] **Step 1: Find the build retry logic**

Read `CodeOrchestratorAgent.cs` and find where build errors are fed back to the LLM for repair. This is typically in the `TryBuild` or compile-retry section where `errorLines` are extracted and sent as a prompt.

- [ ] **Step 2: Add deterministic pre-filter before LLM retry**

Add a static method to check if errors can be fixed without LLM:

```csharp
static string? TryDeterministicFix(string buildOutput)
{
    if (buildOutput.Contains("CS0246") && buildOutput.Contains("IAW.Agents"))
        return "skip"; // invalid namespace — code hallucination, won't fix with retry

    if (buildOutput.Contains("CS0103") && buildOutput.Contains("'Console'"))
        return "add_using_system"; // missing using System — add it

    if (buildOutput.Contains("The process cannot access the file"))
        return "skip"; // file locked — can't fix

    if (buildOutput.Contains("timed out"))
        return "retry"; // transient — retry as-is

    return null; // needs LLM diagnosis
}
```

- [ ] **Step 3: Integrate pre-filter into retry loop**

Before the LLM retry call, check the pre-filter. Find the section where build errors trigger a code regeneration. Add the check before it:

```csharp
var deterministicAction = TryDeterministicFix(fullOutput);
if (deterministicAction == "skip")
{
    // Known unfixable error — don't waste tokens retrying
    break;
}
if (deterministicAction == "retry")
{
    // Transient error — retry without regeneration
    continue;
}
// deterministicAction is null — fall through to LLM repair
```

- [ ] **Step 4: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs
git commit -m "perf: non-LLM pre-filters for deterministic build errors in CodeOrchestrator"
```

---

### Task 3: Compact GetResponse — prevent context window bloat

**Files:**
- Modify: `src/Core/Agents/Agent.cs`

When an agent's `GetResponse` returns a huge result (e.g., full build output, large file), that entire result becomes part of the calling agent's conversation history. This bloats context and wastes tokens on subsequent calls.

- [ ] **Step 1: Add output truncation to GetResponse**

In `src/Core/Agents/Agent.cs`, find the `GetResponse` method (the one that aggregates streaming):

```csharp
public virtual async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
{
    var sb = new System.Text.StringBuilder();
    await foreach (var chunk in GetResponseStream(prompt, cancellationToken))
        sb.Append(chunk);
    return sb.ToString();
}
```

Replace with:
```csharp
public virtual async Task<string> GetResponse(string prompt, CancellationToken cancellationToken = default)
{
    var sb = new System.Text.StringBuilder();
    await foreach (var chunk in GetResponseStream(prompt, cancellationToken))
        sb.Append(chunk);

    var result = sb.ToString();
    if (result.Length > 8000)
    {
        var truncated = result[..8000];
        var lastNewline = truncated.LastIndexOf('\n');
        if (lastNewline > 6000) truncated = truncated[..lastNewline];
        return truncated + "\n...(output truncated at 8KB)";
    }
    return result;
}
```

This truncates at clean line boundaries to avoid breaking structured output.

- [ ] **Step 2: Build and test**

Run: `dotnet build src/Core && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Core/Agents/Agent.cs
git commit -m "perf: truncate GetResponse output to 8KB to prevent context window bloat"
```

---

### Task 4: End-to-end verification

**Files:** None (manual testing)

- [ ] **Step 1: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 2: Restart Aspire assistant**

Restart the assistant resource to pick up changes.

- [ ] **Step 3: Test actionable errors**

Send to Thread: `SendToAgent for a non-existent agent "Banana"`
Expected: "Unknown agent: Banana. Available: Shell, DotNet, FileSystem, Git, Roslyn, GitHub."

- [ ] **Step 4: Test result truncation**

Send to Thread: `Read the file E:\IAW\src\Core\Agents\Agent.cs` (large file)
Expected: Response truncated to ~4KB (from Thread's truncation) instead of full file content flooding context.

- [ ] **Step 5: Verify token usage in traces**

Check Aspire traces — simple tasks should show <5K total tokens with compact results.

- [ ] **Step 6: Commit any fixes**

If issues found, fix and commit.
