# Agent Team v2 — Phase 2: Self-Improving Closed Loop

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable IAW agents to fix themselves on demand — user reports a bug, agents diagnose from traces/code, write a fix, build, test, and deploy via Aspire restart.

**Architecture:** The self-improvement loop is Thread coordinating existing agents: FileSystem reads code → Roslyn analyzes → FileSystem writes fix → DotNet builds/tests → Git commits → Aspire deploys. Aspire also gets a recurring log cleanup job. No new agents needed — just wiring the existing ones together and adding Aspire's cleanup capability.

**Tech Stack:** Orleans grains, Orleans DurableJobs (scheduling), Aspire MCP, C# 13

**Spec:** `docs/superpowers/specs/2026-03-23-agent-team-v2-design.md` (Phase 2 section)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Agents/Infrastructure/AspireAgent.cs` | Modify | Add log cleanup on activation via scheduled job |
| `src/Agents/Infrastructure/IAspire.cs` | Modify | Add CleanLogsAsync interface method |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Add SelfImproveAsync tool for structured self-improvement flow |
| `src/Agents/Orchestration/IThread.cs` | No change | Instructions already describe the flow |

---

### Task 1: Add log cleanup scheduled job to Aspire agent

**Files:**
- Modify: `src/Agents/Infrastructure/IAspire.cs`
- Modify: `src/Agents/Infrastructure/AspireAgent.cs`

The Aspire agent should schedule a recurring cleanup job on activation that clears old logs every 30 minutes.

- [ ] **Step 1: Add CleanLogsAsync to IAspire interface**

In `src/Agents/Infrastructure/IAspire.cs`, add after the existing methods:

```csharp
[Description("Clean old structured logs and console logs for a resource. Prevents log accumulation that makes traces unreadable.")]
Task<string> CleanLogsAsync(string resourceName, CancellationToken ct = default);
```

- [ ] **Step 2: Implement CleanLogsAsync in AspireAgent**

In `src/Agents/Infrastructure/AspireAgent.cs`, add after the `GetLogsAsync` method:

```csharp
public Task<string> CleanLogsAsync(string resourceName, CancellationToken ct = default)
{
    // Aspire MCP doesn't have a direct "clean logs" API.
    // The dashboard auto-manages log retention. We report current log count instead.
    return GetLogsAsync(resourceName, ct);
}
```

Note: Aspire's dashboard manages log retention internally. The agent can't delete logs via MCP, but having the method available means Thread can ask for log status and the agent reports it.

- [ ] **Step 3: Schedule recurring log check on activation**

In `AspireAgent.cs`, override `OnActivateAsync` to schedule a recurring job. The agent already overrides `OnActivateAsync` for MCP connection. Add the scheduling AFTER the existing `ConnectMcpAsync` call:

Find the existing `OnActivateAsync`:
```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken)
{
    await ConnectMcpAsync(cancellationToken);
    await base.OnActivateAsync(cancellationToken);
}
```

Replace with:
```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken)
{
    await ConnectMcpAsync(cancellationToken);
    await base.OnActivateAsync(cancellationToken);

    if (!ScheduledJobs.ContainsKey("log-monitor"))
    {
        await ScheduleRecurringJob("log-monitor", TimeSpan.FromMinutes(30),
            "Check system health and report any resource errors or warnings.", cancellationToken);
    }
}
```

- [ ] **Step 4: Override OnScheduledJobDueAsync for log monitoring**

Add to `AspireAgent.cs`:

```csharp
protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
{
    if (job.Name == "log-monitor")
    {
        logger.LogInformation("Aspire log monitor: checking system health");
        var resources = await ListResourcesAsync(ct);
        if (resources.Contains("Stopped") || resources.Contains("FailedToStart"))
        {
            logger.LogWarning("Aspire log monitor: unhealthy resources detected");
            await PublishAsync("aspire.health.warning", new Dictionary<string, string>
            {
                ["summary"] = "Unhealthy resources detected",
                ["details"] = resources
            }, ct);
        }
        return;
    }

    await base.OnScheduledJobDueAsync(job, ct);
}
```

- [ ] **Step 5: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Agents/Infrastructure/IAspire.cs src/Agents/Infrastructure/AspireAgent.cs
git commit -m "feat: add Aspire log monitor scheduled job and CleanLogs method"
```

---

### Task 2: Add structured self-improvement tool to Thread

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`

Thread's instructions already describe the self-improvement flow. But the LLM may not reliably execute a multi-step chain (read code → analyze → write fix → build → test → commit → deploy) via multiple sequential `SendToAgent` calls. A dedicated `SelfImprove` tool guides the flow more reliably.

- [ ] **Step 1: Add SelfImproveAsync tool to DefineAdditionalTools**

In `ThreadAgent.cs`, find `DefineAdditionalTools` and add a third tool:

```csharp
protected override IReadOnlyList<AITool> DefineAdditionalTools()
{
    return [
        AIFunctionFactory.Create(SendToAgentAsync, "SendToAgent",
            "Send a task to a specific agent by name. The agent handles it autonomously " +
            "with its own LLM and tools. Available agents: Shell, DotNet, FileSystem, Git, Roslyn, GitHub, Aspire."),

        AIFunctionFactory.Create(OrchestrateAsync, "Orchestrate",
            "For complex multi-step tasks requiring coordination across multiple agents. " +
            "NOT needed for single build/run/read/git tasks — use SendToAgent instead."),

        AIFunctionFactory.Create(SelfImproveAsync, "SelfImprove",
            "Fix a bug or improve the IAW system itself. Reads source code, analyzes the issue, " +
            "writes a fix, builds, tests, commits on a branch, and deploys via Aspire restart. " +
            "Use when the user reports a bug in the agent system or asks to improve/fix behavior.")
    ];
}
```

- [ ] **Step 2: Implement SelfImproveAsync**

Add to `ThreadAgent.cs` after `SendToAgentAsync`:

```csharp
private async Task<string> SelfImproveAsync(string issueDescription, CancellationToken ct = default)
{
    logger.LogInformation("SelfImprove: {Issue}", issueDescription[..Math.Min(80, issueDescription.Length)]);
    var steps = new System.Text.StringBuilder();
    var iawRoot = @"E:\IAW";

    try
    {
        // Step 1: Read traces for context
        steps.AppendLine("## Step 1: Reading traces...");
        var traces = await SendToAgentAsync("Aspire", $"Get recent traces for the assistant resource to help diagnose: {issueDescription}", ct);
        steps.AppendLine(traces.Length > 500 ? traces[..500] + "..." : traces);

        // Step 2: Identify relevant source files
        steps.AppendLine("\n## Step 2: Analyzing issue...");
        var analysis = await SendToAgentAsync("Roslyn",
            $"Based on this issue description, which source files in {iawRoot}/src/ are most likely involved? Issue: {issueDescription}\nRecent traces: {traces[..Math.Min(500, traces.Length)]}", ct);
        steps.AppendLine(analysis.Length > 500 ? analysis[..500] + "..." : analysis);

        // Step 3: Read the relevant code
        steps.AppendLine("\n## Step 3: Reading source code...");
        var code = await SendToAgentAsync("FileSystem",
            $"Read the most relevant source files for this issue. {analysis}", ct);
        steps.AppendLine($"Read {code.Length} chars of source code");

        // Step 4: Generate and write fix
        steps.AppendLine("\n## Step 4: Writing fix...");
        var fix = await SendToAgentAsync("Roslyn",
            $"Here is the issue: {issueDescription}\nHere is the code:\n{code[..Math.Min(3000, code.Length)]}\nGenerate the fixed code. Return ONLY the complete fixed file content.", ct);

        if (fix.Contains("```"))
        {
            // Roslyn returned code — extract and write it
            steps.AppendLine("Fix generated. Writing to file...");
            var writeResult = await SendToAgentAsync("FileSystem", $"Write the fix:\n{fix}", ct);
            steps.AppendLine(writeResult);
        }
        else
        {
            steps.AppendLine($"Analysis: {fix[..Math.Min(300, fix.Length)]}");
        }

        // Step 5: Build
        steps.AppendLine("\n## Step 5: Building...");
        var buildResult = await SendToAgentAsync("DotNet", $"Build the solution at {iawRoot}", ct);
        steps.AppendLine(buildResult);

        if (buildResult.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
            buildResult.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            steps.AppendLine("\n**Build failed. Fix not applied.**");
            return steps.ToString();
        }

        // Step 6: Run tests
        steps.AppendLine("\n## Step 6: Running tests...");
        var testResult = await SendToAgentAsync("DotNet", $"Run tests for {iawRoot}", ct);
        steps.AppendLine(testResult);

        // Step 7: Commit
        steps.AppendLine("\n## Step 7: Committing...");
        var commitResult = await SendToAgentAsync("Git",
            $"In {iawRoot}, commit all changes with message: fix: {issueDescription[..Math.Min(50, issueDescription.Length)]}", ct);
        steps.AppendLine(commitResult);

        // Step 8: Deploy
        steps.AppendLine("\n## Step 8: Deploying...");
        var deployResult = await SendToAgentAsync("Aspire", "Restart the assistant resource to deploy the fix", ct);
        steps.AppendLine(deployResult);

        steps.AppendLine("\n## Done! Fix applied and deployed.");
        return steps.ToString();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "SelfImprove failed");
        steps.AppendLine($"\n**Error during self-improvement: {ex.Message}**");
        return steps.ToString();
    }
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests --filter "FullyQualifiedName~Thread" -v minimal`
Expected: 0 errors, all Thread tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs
git commit -m "feat: add SelfImprove tool — closed-loop bug fixing and deployment"
```

---

### Task 3: End-to-end self-improvement test

**Files:** None (manual testing)

- [ ] **Step 1: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 2: Restart Aspire**

Restart the assistant resource via Aspire MCP to pick up all changes.

- [ ] **Step 3: Test self-improvement via Thread**

Send to Thread (via test harness or Telegram):
```
The Aspire agent's instructions say "DO NOT restart resources without being asked" but
this is too cautious. Change it to allow restart when deploying fixes. The file is at
E:\IAW\src\Agents\Infrastructure\IAspire.cs
```

Expected flow:
1. Thread calls `SelfImprove` tool
2. SelfImprove reads traces, analyzes, reads IAspire.cs code, generates fix, writes it
3. DotNet builds the solution
4. DotNet runs tests
5. Git commits the change
6. Aspire restarts the assistant

- [ ] **Step 4: Verify deployment**

After the restart:
- Check that the IAspire.cs file was actually modified
- Check Aspire traces show the self-improvement chain
- Send another message to verify the system is still responsive

- [ ] **Step 5: Test Aspire log monitor**

Wait 30+ minutes or manually trigger:
- Check Aspire structured logs for "Aspire log monitor: checking system health" entries
- Verify no crash or errors from the scheduled job

- [ ] **Step 6: Commit any fixes found during testing**

If issues discovered, fix and commit individually.
