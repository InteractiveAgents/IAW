# PR 7 Review (Codex)

Date: 2026-03-22

PR: `#7`  
Title: `Agent Registry, Orchestration Redesign & Generative UI`

## Verdict

The PR moves the architecture forward, but I would not merge it in the current state.

The main problems are not style issues. They are runtime-behavior regressions in the new registry/scheduling/delegation model that can make the system lose its catalog, duplicate jobs, or silently fail to deliver delegated results outside Telegram.

## Findings

### 1. High: durable jobs are re-scheduled on every activation, which duplicates work

Files:

- `src/Core/Agents/Agent.Scheduling.cs:19`
- `src/Core/Agents/Agent.Scheduling.cs:32`
- `src/Core/Agents/Agent.Scheduling.cs:79`
- `src/Core/Agents/Agent.Scheduling.cs:140`

Why this is a problem:

- `ScheduleJob` and `ScheduleRecurringJob` already create a durable job and persist its `DurableJobId` / `DurableJobShardId`.
- `ExecuteJobAsync` also schedules the next durable job for recurring work after each execution.
- `OnActivateAsync` now calls `RescheduleExistingJobsAsync`, and that method unconditionally calls `JobManager.ScheduleJobAsync(...)` again for every saved job.
- Nothing cancels or checks the previously scheduled durable job before the new one is created.

Impact:

- One-shot jobs can run multiple times after reactivation.
- Recurring jobs can multiply across activations and produce duplicate executions indefinitely.
- The more an agent deactivates/reactivates, the more duplicate work you create.

What I would change:

- Treat the stored `DurableJobId` / `DurableJobShardId` as the source of truth.
- Only schedule on activation when a persisted job record is missing durable-job coordinates.
- If recovery really needs re-scheduling, explicitly cancel the old durable job before creating a replacement.

### 2. High: the new registry is activation-local memory only, so it can go empty after deactivation

Files:

- `src/Core/Registry/AgentRegistryGrain.cs:7`
- `src/Core/Registry/AgentRegistryGrain.cs:9`
- `src/Core/Registry/AgentRegistrationStartupTask.cs:7`
- `src/Core/Registry/AgentRegistrationStartupTask.cs:15`

Why this is a problem:

- `AgentRegistryGrain` now stores everything in a plain in-memory `Dictionary<string, AgentRecord>`.
- The catalog is populated by `AgentRegistrationStartupTask`, which runs at silo startup.
- There is no durable state in the registry grain and no repopulation logic on grain activation.

Impact:

- Once the registry grain is garbage-collected for idleness, its `_records` dictionary is gone.
- The next activation comes back empty, so `SearchAsync`, `GetAllAsync`, and `ToPromptStringAsync` return nothing until the whole silo restarts.
- That breaks agent selection, MCP `agent_list_all`, and the orchestrator prompt that depends on the catalog.

What I would change:

- Either make the registry durable again, or rebuild its state on `OnActivateAsync`.
- If the intent is “startup discovery only,” the registry grain still needs a durable backing store or a deterministic reload path.

### 3. High: delegated work only completes in Telegram; MCP and DevUI get the placeholder but never the result

Files:

- `src/Agents/Orchestration/ThreadAgent.cs:59`
- `src/Agents/Orchestration/ThreadAgent.cs:65`
- `src/Agents/Orchestration/ThreadAgent.cs:160`
- `src/IAW.MCP/Tools/AgentTools.cs:61`
- `src/IAW.MCP/Tools/AgentTools.cs:68`
- `src/DevUI/OrleansAgentChatClient.cs:17`
- `src/DevUI/OrleansAgentChatClient.cs:52`
- `src/Clients.Telegram/StreamSubscriber.cs:41`
- `src/Clients.Telegram/StreamSubscriber.cs:58`

Why this is a problem:

- `ThreadAgent` now delegates operational work by scheduling a job and immediately returning `"Task ... submitted"`.
- The actual completion signal is published later via `job.completed` / `orchestration.progress`.
- Telegram subscribes to those streams and renders the eventual result.
- MCP `assistant_chat` and DevUI only wait for `thread.GetResponse(...)` / `thread.GetResponseStream(...)`, which means they only get the initial placeholder text.

Impact:

- A coding/build/git request made through MCP or DevUI looks accepted but never returns the final answer.
- The new async delegation model is effectively Telegram-only, even though the thread abstraction is being presented as general-purpose.

What I would change:

- Either keep delegation synchronous for MCP/DevUI callers, or expose a first-class async task/result API and teach those clients to poll/subscribe.
- At minimum, do not claim the thread flow is wired end-to-end for MCP while delegated results are only consumed by Telegram.

### 4. Medium: the workspace safety boundary was removed from typed file operations and is now bypassable as an AI tool

Files:

- `src/Core/Agents/Agent.State.cs:28`
- `src/Core/Agents/Agent.State.cs:42`
- `src/Agents/Infrastructure/FileSystemAgent.cs:25`
- `src/Agents/Infrastructure/FileSystemAgent.cs:45`
- `src/Agents/Infrastructure/FileSystemAgent.cs:69`
- `src/Agents/Infrastructure/FileSystemAgent.cs:75`
- `src/Agents/Infrastructure/FileSystemAgent.cs:94`
- `src/Core/Agents/Agent.Tools.cs:45`
- `src/Core/Tools/FileTools.cs:28`
- `src/Core/Tools/FileTools.cs:83`

Why this is a problem:

- `ResolvePathAgainstWorkspace` now resolves rooted paths directly and no longer enforces containment.
- `ValidatePathWithinWorkspace` has been reduced to a no-op.
- `FileSystemAgent` typed methods use that relaxed resolver for read, write, list, search, and compare.
- The new auto-tool discovery registers interface methods as LLM tools, so those typed methods are now reachable automatically.
- Meanwhile `FileTools.WriteFileAsync` still explicitly enforces `ValidateInsideWorkspace`, so the PR now has two inconsistent file-access models.

Impact:

- A caller can hand an absolute path to `IFileSystem` and operate outside the configured workspace.
- The old workspace boundary still exists in the older helper tools, but the new typed path bypasses it.
- That is a meaningful safety regression, not just an implementation detail.

What I would change:

- Restore containment checks for the default typed file API.
- If out-of-workspace access is intentional, expose it as a separate privileged interface/tool instead of silently weakening the main one.

### 5. Medium: truncating the serialized orchestration result corrupts downstream parsing

Files:

- `src/Agents/Orchestration/ThreadAgent.cs:156`
- `src/Agents/Orchestration/ThreadAgent.cs:160`
- `src/Clients.Telegram/TelegramBotService.cs:385`
- `src/Clients.Telegram/TelegramBotService.cs:406`
- `src/Clients.Telegram/TelegramBotService.cs:486`
- `src/Clients.Telegram/TelegramBotService.cs:514`

Why this is a problem:

- `ThreadAgent` serializes the orchestration result to JSON, then truncates the string to 4000 characters before publishing it.
- `TelegramBotService.FormatOrchestrationResult` expects valid `OrchestrationResult` JSON so it can recover `TaskId`, summary, metrics, and artifacts.
- When truncation cuts through the JSON payload, deserialization fails and Telegram falls back to raw text.

Impact:

- Long failures lose structured rendering.
- Progress-message correlation via `TaskId` can be lost.
- The UX degrades exactly on the cases where the extra structure is most useful: large errors and multi-artifact outputs.

What I would change:

- Publish structured fields instead of a truncated serialized blob.
- If you must limit size, truncate `ErrorDetail` before serialization rather than truncating the final JSON string.

## Consistency Notes

The broad direction is coherent:

- flat namespace taxonomy
- static interface metadata
- richer UI parts
- selector plus orchestrator split

The inconsistencies are in the runtime contracts:

- durable jobs are treated as both durable and activation-local
- the registry is treated as both global state and activation-local state
- delegation is treated as both synchronous chat behavior and asynchronous background work
- file safety is enforced in one API path and removed in another

Those contradictions are what create the bugs above.

## Test / Verification Notes

I attempted local verification during review:

- `dotnet build test/Core.Tests/IAW.Core.Tests.csproj --no-restore -v minimal`
- `dotnet build test/E2E.Tests/E2E.Tests.csproj --no-restore -v minimal`
- `dotnet test IAW.slnx --no-restore -v minimal`

The sandbox blocked writes to build outputs, and the escalated solution test run did not return usable console output in this environment. Because of that, this review is primarily based on static inspection of the changed code paths and their call graph.

## Recommended Merge Gate

I would block merge until at least these are fixed:

1. Remove durable-job duplication on activation.
2. Make the agent registry survive grain deactivation.
3. Define a non-Telegram result path for delegated thread work.

After those three are fixed, I would re-review the file-system boundary change as a separate policy decision rather than letting it slip in as an incidental side effect of the refactor.
