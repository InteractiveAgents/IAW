# Async Delegation via DurableJobs

## Problem

The ThreadAgent's `DelegateAsync` tool runs AgentSelector + CodeOrchestrator inline within the LLM tool callback. The CancellationToken from the outer `GetResponse` call cascades through the entire chain — when the Orleans ResponseTimeout expires, it kills the Anthropic HTTP call mid-flight with `OperationCanceledException`.

This never happened in master because tools were millisecond operations (SetWorkspace, GetWorkspace). The Delegate tool is the first long-running tool — it chains multiple cross-grain LLM calls that can take minutes.

## Design

### Core Change

`DelegateAsync` schedules a one-shot DurableJob with zero delay instead of blocking. The job runs independently with `CancellationToken.None` — completely decoupled from the caller's lifecycle.

### Flow

```
User sends message → Thread LLM → calls Delegate tool
  → ScheduleJob("dlg-abc123", TimeSpan.Zero, "[DELEGATE]request")
  → Persisted to Azure Blob (DurableJobs storage)
  → Returns immediately: "Task dlg-abc123 submitted. Working on your request."
  → Thread LLM tells user "I'm processing your request"
  → MCP/Telegram response completes in seconds

[Independently, Orleans DurableJobs fires:]
  → ExecuteJobAsync → OnScheduledJobDueAsync
  → Detect [DELEGATE] prefix
  → selector.SelectAsync(request, CancellationToken.None)
  → ExecuteSelection(result, request, CancellationToken.None)
  → Store result in ScheduledJobItem.LastResult
  → Publish JobCompleted event with projectKey for topic routing
  → Telegram StreamSubscriber sends result to the correct topic
```

### What changes

**`src/Agents/Orchestration/ThreadAgent.cs`** — Two changes:

1. `DelegateAsync` — replace inline execution with `ScheduleJob` call. Returns immediately.

2. Override `OnScheduledJobDueAsync` — detect `[DELEGATE]` prefix, run AgentSelector + ExecuteSelection with `CancellationToken.None`. Publish `JobCompleted` event with:
   - `"projectKey"` = `this.GetPrimaryKeyString()` — routes to correct Telegram topic
   - `"jobName"` = `job.Name` — displayed as title
   - `"result"` = delegation result — displayed as body

   **Important:** Use camelCase keys (`"jobName"`, `"result"`) — the Telegram StreamSubscriber reads camelCase, not PascalCase. The base class `OnScheduledJobDueAsync` uses PascalCase (`"JobName"`, `"Result"`) which is a pre-existing mismatch that we fix in the override.

### Reentrancy note

ThreadAgent is NOT `[Reentrant]` and does not need to be for this pattern. The `DelegateAsync` tool completes in milliseconds (just a `ScheduleJob` call). The outer `GetResponse` call finishes within seconds. The DurableJob fires asynchronously after the grain turn completes — no overlap, no deadlock risk. If the grain is busy when the job fires, Orleans queues the job callback normally.

### What does NOT change

- `Agent.Scheduling.cs` — existing DurableJobs infrastructure handles everything: job persistence, one-shot cleanup, crash recovery via `RescheduleExistingJobsAsync`
- `StreamSubscriber.cs` — already subscribes to `JobCompleted` events and reads `projectKey`/`jobName`/`result` (camelCase). Routing to Telegram topics already works.
- `TelegramBotService.SendJobResultAsync` — already parses `projectKey` as `userId/slug` to find the right topic
- `IAgent`, `IThread` interfaces — no contract changes
- MCP, DevUI — no client changes

### Recovery

**Silo crash during delegation:** Job was persisted to Azure Blob before execution. On grain reactivation, `RescheduleExistingJobsAsync` finds the pending job and re-fires it. Delegation runs again from scratch.

**AgentSelector fails:** Exception caught, error message stored as `LastResult`, published as `JobCompleted`. User gets notification with error.

**CodeOrchestrator takes 10+ minutes:** No timeout cascade — `CancellationToken.None` decouples from everything. The orchestrator's own `[ResponseTimeout("00:15:00")]` applies independently.

### Why DurableJobs

- Already configured in IAW (Azure Blob storage, `UseAzureBlobDurableJobs`)
- Already used by memory agents for scheduled maintenance
- One-shot jobs with zero delay supported natively
- Crash recovery built in via `RescheduleExistingJobsAsync`
- Result notification via existing `JobCompleted` stream event
- No new packages, no new infrastructure

### Out of scope

- MCP `assistant_chat` returns "task submitted" — actual result arrives asynchronously via stream. Real-time MCP result delivery is a separate enhancement.
- CodeOrchestrator code generation quality — separate issue, already addressed with Opus model switch.
