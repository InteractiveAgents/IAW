# Async Delegation via DurableJobs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the CancellationToken cascade bug by making ThreadAgent's Delegate tool schedule a DurableJob instead of blocking inline.

**Architecture:** `DelegateAsync` schedules a zero-delay one-shot DurableJob and returns immediately. The job fires independently via Orleans DurableJobs with no cascading CancellationToken. Result is delivered via the existing `JobCompleted` stream event to Telegram/MCP.

**Tech Stack:** C# / .NET 11, Orleans 10 DurableJobs, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-21-async-delegation-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Core/IAWConstants.cs` | Modify | Add payload key constants and delegation prefix |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Rewrite DelegateAsync + add OnScheduledJobDueAsync override |
| `test/Core.Tests/ThreadDelegateToolTests.cs` | Modify | Update tests for async delegation behavior |

---

### Task 1: Add Constants

No magic strings. Add payload key constants and the delegation prefix to `IAWConstants`.

**Files:**
- Modify: `src/Core/IAWConstants.cs`

- [ ] **Step 1: Add constants to IAWConstants**

Add a `PayloadKeys` class and a `DelegationPrefix` constant:

```csharp
public static class PayloadKeys
{
    public const string ProjectKey = "projectKey";
    public const string JobName = "jobName";
    public const string Result = "result";
}

public const string DelegationPrefix = "[DELEGATE]";
```

Place `PayloadKeys` after the existing `StateKeys` class. Place `DelegationPrefix` at the top level of `IAWConstants` (next to `StreamProvider`).

- [ ] **Step 2: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Core/IAWConstants.cs
git commit -m "feat: add PayloadKeys and DelegationPrefix constants to IAWConstants"
```

---

### Task 2: Rewrite DelegateAsync to Schedule DurableJob

Replace the blocking inline delegation with a zero-delay DurableJob.

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`

- [ ] **Step 1: Read the current ThreadAgent.cs**

Read the file to understand current `DelegateAsync` (lines 54-85) and `ExecuteSelection` (lines 87-105).

- [ ] **Step 2: Replace DelegateAsync**

Replace the entire `DelegateAsync` method with:

```csharp
private async Task<string> DelegateAsync(string request, CancellationToken ct = default)
{
    var taskId = $"dlg-{Guid.NewGuid().ToString("N")[..8]}";
    logger.LogInformation("Delegate: scheduling job {TaskId} for: {Request}",
        taskId, request[..Math.Min(80, request.Length)]);

    await ScheduleJob(taskId, TimeSpan.Zero, $"{IAWConstants.DelegationPrefix}{request}", ct);
    return $"Task {taskId} submitted. I'm working on your request and will deliver results shortly.";
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs
git commit -m "feat: DelegateAsync schedules DurableJob instead of blocking inline"
```

---

### Task 3: Add OnScheduledJobDueAsync Override

Handle delegation jobs in the DurableJob callback with `CancellationToken.None`.

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`

- [ ] **Step 1: Add the override**

Add after the `FormatClarificationResponse` method (after line ~120):

```csharp
protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
{
    if (!job.Prompt.StartsWith(IAWConstants.DelegationPrefix))
    {
        await base.OnScheduledJobDueAsync(job, ct);
        return;
    }

    var request = job.Prompt[IAWConstants.DelegationPrefix.Length..];
    logger.LogInformation("DelegateJob: executing {JobName} for: {Request}",
        job.Name, request[..Math.Min(80, request.Length)]);

    string delegationResult;
    try
    {
        var selector = GrainFactory.Get<IAgentSelector>();
        var selection = await selector.SelectAsync(request, CancellationToken.None);

        logger.LogInformation("DelegateJob: selector returned Status={Status}, Agents=[{Agents}]",
            selection.Status, string.Join(",", selection.SelectedAgents));

        delegationResult = selection.Status switch
        {
            SelectionStatus.Ready => await ExecuteSelection(selection, request, CancellationToken.None),
            SelectionStatus.CannotHandle => selection.Plan ?? "The agent system cannot handle this request.",
            SelectionStatus.NeedsClarification => FormatClarificationResponse(selection),
            _ => "Unexpected selection status."
        };
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "DelegateJob: FAILED {JobName}", job.Name);
        delegationResult = $"Delegation failed: {ex.GetType().Name}: {ex.Message}";
    }

    var updated = job with { LastRunAt = DateTimeOffset.UtcNow, LastResult = delegationResult };
    durableState.ScheduledJobs[job.Name] = updated;

    logger.LogInformation("DelegateJob: completed {JobName}, result length: {Length}",
        job.Name, delegationResult.Length);

    var truncatedResult = delegationResult.Length > 4000
        ? delegationResult[..4000] + "\n...(truncated)"
        : delegationResult;

    await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, string>
    {
        [IAWConstants.PayloadKeys.ProjectKey] = this.GetPrimaryKeyString(),
        [IAWConstants.PayloadKeys.JobName] = job.Name,
        [IAWConstants.PayloadKeys.Result] = truncatedResult
    }, CancellationToken.None);
}
```

Key points:
- `CancellationToken.None` on ALL inner grain calls — decoupled from caller
- Uses `IAWConstants.DelegationPrefix` — no magic strings
- Uses `IAWConstants.PayloadKeys.*` — camelCase keys matching StreamSubscriber
- Uses `IAWConstants.Events.JobCompleted` — existing event constant
- Truncates result to 4000 chars for Telegram (4096 limit)
- Logging at every decision point for diagnostics

- [ ] **Step 2: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs
git commit -m "feat: add OnScheduledJobDueAsync override for async delegation execution"
```

---

### Task 4: Update Tests

Update the existing ThreadDelegateToolTests to verify the new async behavior.

**Files:**
- Modify: `test/Core.Tests/ThreadDelegateToolTests.cs`

- [ ] **Step 1: Read current tests**

Read `test/Core.Tests/ThreadDelegateToolTests.cs`.

- [ ] **Step 2: Update tests**

The `GetResponse_WithDelegationRequest_ReturnsResponse` test currently checks that the response is not empty. With the async change, the response will contain "Task submitted" text (the LLM sees the tool result and generates a response about it — MockChatClient returns "mock-response" regardless).

The test still passes because `Assert.NotEmpty(response)` is satisfied by "mock-response". But add a focused test for the scheduling behavior:

```csharp
[Fact]
public async Task Delegate_SchedulesJobAndReturnsImmediately()
{
    var ct = TestContext.Current.CancellationToken;
    var thread = Agent(UniqueId("dlg-async"));

    // The response should come back quickly (no blocking on AgentSelector)
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var response = await thread.GetResponse("check git status", ct);
    sw.Stop();

    Assert.NotNull(response);
    Assert.NotEmpty(response);
    // Should complete in seconds, not minutes (proves no blocking)
    Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
        $"Response took {sw.Elapsed} — should be fast since delegation is async");
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~Thread" -v m`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git add test/Core.Tests/ThreadDelegateToolTests.cs
git commit -m "test: update delegation tests for async DurableJob behavior"
```

---

### Task 5: Full Build, Test, and Verify

**Files:** None (verification only)

- [ ] **Step 1: Build the solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx -v m`
Expected: All pass.

- [ ] **Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve issues from async delegation changes"
```
