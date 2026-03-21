# Telegram UX Overhaul — Unified Message Delivery

**Date:** 2026-03-21
**Status:** Final
**Scope:** TelegramBotService, StreamSubscriber, ThreadAgent, CodeOrchestratorAgent, Agent.Scheduling, website docs

## Problem

The Telegram personal assistant has six UX issues traced to architectural gaps:

1. **Lost user intent** — ThreadAgent's `Delegate` tool LLM rewrites the user's message, stripping paths and specifics (e.g. `D:\IAW\Calc` becomes "current workspace")
2. **60s dead silence** — delegated tasks produce no feedback until completion; `OrchestrationProgress` events are subscribed but never published
3. **Raw result dumps** — `SendJobResultAsync` dumps unformatted strings; bypasses TelegramUIAgent entirely (no buttons, no MarkdownV2)
4. **No follow-up actions** — job results have no inline buttons; the existing TelegramUIAgent + RichOutput pipeline is unused for async results
5. **Streaming flicker** — 500ms edit interval causes jittery Telegram message updates
6. **Dead wiring and payload bugs** — four StreamSubscriber handlers have no publishers; base `Agent.Scheduling` publishes `job.completed` with wrong payload keys

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Progress delivery | New message on first event, edit in-place after | Telegram push notifications only fire on new messages, not edits. Users leave the chat during 60s tasks and need to be pulled back. |
| Result formatting | Structured `OrchestrationResult` + TelegramUIAgent for buttons | Instant structured rendering (no LLM for status/artifacts), existing TelegramUIAgent generates contextual suggestion buttons from the result text |
| Follow-up buttons | Reuse existing TelegramUIAgent → RichOutput → SuggestionPart pipeline | Already implemented for streaming responses; just needs to be wired for job results |
| Error presentation | LLM-assisted via TelegramUIAgent | Template for status/artifacts, but error diagnosis benefits from LLM summarization |
| Streaming rate | 1500ms | Telegram API is not designed for rapid-fire edits; 1.5s reduces flicker without feeling sluggish |
| Dead code | Remove unconnected subscriptions | Keep `orchestration.progress` (will be published); remove `orchestration.completed`, `dashboard.changed`, `approval.requested`, `wizard.started` — re-add when publishers exist |

## Architecture: Two Delivery Paths

All messages to Telegram flow through exactly two paths:

```
PATH 1: SYNCHRONOUS STREAMING
  User message → Thread.GetResponseStream → TelegramBotService edits in real-time
  → TelegramUIAgent formats final text → RichOutput with buttons

PATH 2: ASYNCHRONOUS EVENT DELIVERY
  Agent publishes event → Orleans stream → StreamSubscriber → TelegramBotService
  Sub-types:
    a) Progress updates  → edit existing message in-place
    b) Job results       → TelegramUIAgent → RichOutput with buttons
    c) Notifications     → send new message (generic alerts)
```

### Path 1: Streaming (minor changes)

Current flow is correct. Two changes:

- Edit interval: 500ms → 1500ms
- ThreadAgent `Delegate` tool: pass verbatim user message, not LLM rewrite

### Path 2a: Progress Updates

CodeOrchestratorAgent publishes `OrchestrationProgress` events at each phase. StreamSubscriber routes them to a new `TelegramBotService.SendProgressAsync` method.

**Event payload (all keys use camelCase via PayloadKeys constants):**

```csharp
await PublishAsync(IAWConstants.Events.OrchestrationProgress, new Dictionary<string, string>
{
    [IAWConstants.PayloadKeys.ProjectKey] = callerProjectKey,
    [IAWConstants.PayloadKeys.TaskId] = taskId,
    [IAWConstants.PayloadKeys.Phase] = phase,      // "planning", "building", "executing", "retrying"
    [IAWConstants.PayloadKeys.Message] = message    // human-readable status line
}, ct);
```

**TelegramBotService.SendProgressAsync:**

```csharp
private readonly ConcurrentDictionary<string, (long ChatId, int MessageId, int? TopicId)> _progressMessages = new();

async Task SendProgressAsync(string projectKey, string taskId, string phase, string message, CancellationToken ct)
{
    // resolve chatId + topicId from projectKey (same pattern as SendJobResultAsync)
    var parts = projectKey.Split('/');
    // ... resolve userId, slug, chatId, topicId ...

    if (_progressMessages.TryGetValue(taskId, out var existing))
    {
        try { await botClient.EditMessageTextAsync(existing.ChatId, existing.MessageId, $"⚙️ {message}"); }
        catch (BotRequestException) { } // message may have been deleted by user
    }
    else
    {
        var sent = await botClient.SendMessageAsync(chatId, $"⚙️ {message}", messageThreadId: topicId);
        _progressMessages[taskId] = (chatId, sent.MessageId, topicId);
    }
}
```

**Note:** `_progressMessages` is in-memory and ephemeral. If the Telegram client process restarts between a progress event and the job completion event, the mapping is lost. In that case, `SendJobResultAsync` will send a new message instead of editing the progress message. This is acceptable — the user still sees the result, just as a separate message.

### Path 2b: Job Results

ThreadAgent serializes `OrchestrationResult` as JSON in the `job.completed` payload. `SendJobResultAsync` parses it, formats a structured message, then routes through TelegramUIAgent for buttons.

**Message lifecycle:**

```
Progress message exists for taskId?
  YES → edit that message with the final RichOutput (progress → result in same message)
  NO  → send a new message with the RichOutput

Cleanup: remove taskId from _progressMessages dict
```

**Formatting logic (no LLM):**

```
Success:
  ✅ {summary}
  📁 {artifact paths, one per line}
  ⏱ {duration if available}

Failure:
  ❌ {summary}
  {error detail, truncated to 500 chars}
```

Then pass the formatted text to `TelegramUIAgent.FormatResponse()` to get contextual suggestion buttons (SuggestionPart). The TelegramUIAgent's LLM sees the pre-formatted text and generates appropriate follow-up actions — it does not re-format the structure (status icon, artifacts), only adds a `parts` array with suggestions. If TelegramUIAgent returns no parts, the structured text is rendered directly via `EditWithMarkdown`. Finally, if parts exist, render via existing `RenderRichOutput`.

### Path 2c: Notifications

Keep `notification.sent` → `SendNotificationAsync` as-is. This is the only remaining generic event handler.

## New Type: OrchestrationResult

Replace the raw string return from `CodeOrchestratorAgent.ExecuteCodeOrchestration` with a structured record:

```csharp
[GenerateSerializer]
public record OrchestrationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Summary,
    [property: Id(2)] string WorkspacePath,
    [property: Id(3)] List<string> Artifacts,
    [property: Id(4)] Dictionary<string, string>? Metrics,
    [property: Id(5)] string? ErrorDetail);
```

Uses `List<string>` for `Artifacts` to match existing `[GenerateSerializer]` patterns in the codebase (e.g. `SelectionResult`).

**Location:** `src/Core/Contracts/OrchestrationResult.cs`

**Serialization in event payload:** ThreadAgent serializes the `OrchestrationResult` to JSON and includes it in the `result` payload key. `SendJobResultAsync` deserializes it. Falls back to displaying the raw string if deserialization fails (backward compat with single-agent delegation which returns plain text).

## Changes by File

### src/Core/Contracts/OrchestrationResult.cs (new)

New `OrchestrationResult` record.

### src/Core/IAWConstants.cs

- Remove `OrchestrationCompleted` event constant (confirmed: no publishers exist anywhere in the codebase, safe to remove)
- Add payload key constants using camelCase to match existing pattern (`projectKey`, `jobName`, `result`):
  - `TaskId = "taskId"`
  - `Phase = "phase"`
  - `Message = "message"`

### src/Core/Agents/Agent.Scheduling.cs

Fix base class `OnScheduledJobDueAsync` to use `PayloadKeys` constants and correct the casing bug (`"JobName"` → `"jobName"`, `"Result"` → `"result"`):

```csharp
// Before (wrong casing, missing projectKey):
["JobName"] = job.Name, ["Result"] = result

// After (matches PayloadKeys constants and StreamSubscriber expectations):
[PayloadKeys.ProjectKey] = this.GetPrimaryKeyString(),
[PayloadKeys.JobName] = job.Name,
[PayloadKeys.Result] = result
```

**Note:** For the base class, `this.GetPrimaryKeyString()` returns the grain ID, which is only a valid Telegram-routable projectKey for ThreadAgent (`{telegramId}/{slug}`). For other agents, the projectKey will be meaningless for Telegram routing — `SendJobResultAsync` will fail to parse it and log a warning (existing behavior). This is acceptable because only ThreadAgent uses the delegation/job flow that reaches Telegram. The fix ensures casing consistency regardless of which agent publishes.

### src/Agents/Orchestration/CodeOrchestratorAgent.cs

1. Change `ExecuteCodeOrchestration` return type from `string` to `OrchestrationResult`

2. **Update `GetResponse` override** (line 182-186) which also calls `ExecuteCodeOrchestration` with `[EXECUTE_CODE]` prefix. This code path is used by the MCP server and DevUI. Convert the `OrchestrationResult` back to a readable string:

   ```csharp
   public override async Task<string> GetResponse(string prompt, CancellationToken ct = default)
   {
       if (prompt.StartsWith("[EXECUTE_CODE]"))
       {
           var result = await ExecuteCodeOrchestration(prompt["[EXECUTE_CODE]\n".Length..], [], "", ct);
           return result.Success
               ? $"Completed. Workspace: {result.WorkspacePath}\nSummary: {result.Summary}"
               : $"Failed. {result.ErrorDetail ?? result.Summary}";
       }
       return await base.GetResponse(prompt, ct);
   }
   ```

3. Publish `OrchestrationProgress` events at each phase:
   - After `GenerateCode()`: phase "planning"
   - Before `TryBuild()`: phase "building"
   - Before `ExecuteProject()`: phase "executing"
   - On retry: phase "retrying"
   - Progress events are only published when `projectKey` is non-empty (skipped for MCP/DevUI path where no Telegram chat exists)

4. Accept `projectKey` parameter so progress events can be routed to the correct Telegram chat

5. Build `OrchestrationResult` from execution results instead of returning formatted strings

6. Parse `result.json` if present to populate `Summary`, `Artifacts`, `Metrics`. If `result.json` is missing, use the last execution output as `Summary`

### src/Agents/Orchestration/ThreadAgent.cs

1. **Preserve verbatim user message in Delegate tool:**

   Read the most recent user message from the durable chat history. When `GetResponseStream` runs, the base `Agent` adds the user's verbatim text to the session and durable history BEFORE the LLM processes it. By the time the LLM calls `DelegateAsync`, the history already contains the original text.

   In `ExecuteSelection`, read it from history:

   ```csharp
   var lastUserMsg = durableState.History.LastOrDefault(m => m.Role == "user");
   var userMessage = lastUserMsg?.Text ?? request;
   var plan = $"USER REQUEST: {userMessage}\n\nPLAN:\n{selectorPlan}";
   ```

   **No core Agent.cs change needed.** No method overrides, no `new` hiding. The durable history is the authoritative source of the user's verbatim input. Orleans grains are single-threaded, so the history entry exists by the time the tool function executes.

   This ensures the CodeOrchestrator sees the original path (`D:\IAW\Calc`) even if the LLM's Delegate tool call rewrote it to "current workspace."

2. **Pass projectKey to CodeOrchestrator:**

   The CodeOrchestrator's grain ID is already the threadId (`{telegramId}/{slug}`), which equals the projectKey. However, `ExecuteCodeOrchestration` needs the projectKey explicitly because the orchestrator's `this.GetPrimaryKeyString()` includes a prefix like `code-orchestrator/{threadId}/ICodeOrchestrator` — it is NOT the bare projectKey. So pass it as a parameter:

   ```csharp
   var orchestrator = GrainFactory.Get<ICodeOrchestrator>(threadId);
   return await orchestrator.ExecuteCodeOrchestration(plan, selection.SelectedAgents, this.GetPrimaryKeyString(), ct);
   ```

3. **Updated `ExecuteSelection` method:**

   `ExecuteSelection` always returns a `string` for the job.completed payload. Multi-agent results are JSON-serialized `OrchestrationResult`; single-agent results are plain text.

   ```csharp
   private async Task<string> ExecuteSelection(SelectionResult selection, string request, CancellationToken ct)
   {
       var threadId = this.GetPrimaryKeyString();
       var lastUserMsg = durableState.History.LastOrDefault(m => m.Role == "user");
       var userMessage = lastUserMsg?.Text ?? request;

       if (selection.SelectedAgents.Count == 1)
       {
           var agentInterfaceName = selection.SelectedAgents[0];
           var interfaceType = AgentInterfaceResolver.Resolve(agentInterfaceName);
           if (interfaceType is null)
               return $"Could not resolve agent: {agentInterfaceName}";
           var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");
           return await agent.GetResponse(request, ct);
       }

       var orchestrator = GrainFactory.Get<ICodeOrchestrator>(threadId);
       var selectorPlan = selection.Plan ?? $"Agents: {string.Join(", ", selection.SelectedAgents)}";
       var plan = $"USER REQUEST: {userMessage}\n\nPLAN:\n{selectorPlan}";
       var result = await orchestrator.ExecuteCodeOrchestration(plan, selection.SelectedAgents, threadId, ct);
       return JsonSerializer.Serialize(result);
   }
   ```

   `SendJobResultAsync` tries `JsonSerializer.Deserialize<OrchestrationResult>(result)`. On success, formats the structured card. On failure (single-agent plain string), falls back to displaying the raw text via TelegramUIAgent.

### src/Agents/Orchestration/IThread.cs

Update `AgentInstructions` for the Delegate tool: add instruction to preserve exact paths, filenames, and locations from the user's message.

### src/Core/Contracts/ICodeOrchestrator.cs

Update `ExecuteCodeOrchestration` signature:

```csharp
Task<OrchestrationResult> ExecuteCodeOrchestration(
    string prompt,
    IReadOnlyList<string> selectedAgents,
    string projectKey,
    CancellationToken ct = default);
```

### src/Clients.Telegram/TelegramBotService.cs

1. **New `SendProgressAsync` method** with `ConcurrentDictionary<string, (long, int, int?)>` for tracking progress message IDs per taskId.

2. **Refactored `SendJobResultAsync`:**
   - Try to deserialize `OrchestrationResult` from the result payload
   - Format structured text (status icon + summary + artifacts)
   - Route through `TelegramUIAgent.FormatResponse()` for buttons
   - If a progress message exists for this taskId → edit it with RichOutput
   - If no progress message → send new message
   - Cleanup progress tracking entry
   - Fallback: if deserialization fails, display raw text (single-agent results)

3. **Streaming rate:** Extract the hardcoded `500` on line 629 to a named constant `StreamingEditIntervalMs = 1500`. This is a tuning parameter that has already changed once.

### src/Clients.Telegram/StreamSubscriber.cs

1. **Rewire `orchestration.progress` handler** to call `SendProgressAsync` instead of `SendNotificationAsync`:

   ```csharp
   var progressStream = streamProvider.GetStream<AgentEvent>(
       StreamId.Create(IAWConstants.StreamProvider, IAWConstants.Events.OrchestrationProgress));
   await progressStream.SubscribeAsync(async (evt, token) =>
   {
       var projectKey = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.ProjectKey)?.ToString() ?? "";
       var taskId = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.TaskId)?.ToString() ?? "";
       var phase = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.Phase)?.ToString() ?? "";
       var message = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.Message)?.ToString() ?? "";
       await botService.SendProgressAsync(projectKey, taskId, phase, message, ct);
   });
   ```

2. **Remove dead subscriptions** (the handler code in StreamSubscriber only):
   - `orchestration.completed` (functionality merged into job.completed)
   - `dashboard.changed` (no publisher exists)
   - `approval.requested` (no publisher exists)
   - `wizard.started` (no publisher exists)

   **Keep** the corresponding constants in `IAWConstants.Events` (`ApprovalRequested`, `DashboardChanged`) and the handler methods in `TelegramBotService` (`SendApprovalAsync`, `SendWizardStepAsync`). These are complete implementations that will be re-wired when publishers are added. Only the StreamSubscriber subscription glue is removed.

3. **Remove `ScheduleDebouncedDashboardUpdate`** method and `_dashboardDebounce` field (dead code, tied to the removed `dashboard.changed` subscription).

### src/Agents/Orchestration/ITelegramUI.cs

No changes. Existing `FormatResponse` method is reused as-is.

### src/Agents/Orchestration/TelegramUIAgent.cs

No changes. Existing implementation handles the formatting.

## Progress Message Lifecycle

```
CodeOrchestrator publishes          StreamSubscriber          TelegramBotService
  OrchestrationProgress               routes to              SendProgressAsync
  { phase: "planning" }           ──────────────►           → NEW message "⚙️ Planning..."
                                                              → store taskId → msgId

  OrchestrationProgress
  { phase: "building" }            ──────────────►           → EDIT "⚙️ Building..."

  OrchestrationProgress
  { phase: "executing" }           ──────────────►           → EDIT "⚙️ Running..."

ThreadAgent publishes
  JobCompleted                     ──────────────►           SendJobResultAsync
  { result: OrchestrationResult }                            → find progress msgId
                                                              → TelegramUIAgent.FormatResponse
                                                              → EDIT msgId with RichOutput
                                                              → cleanup tracking dict
```

## User-Visible Result Examples

### Success

```
✅ Calculator app ready
📁 D:\IAW\Calc\CalculatorApp.csproj
Built — 0 errors, 0 warnings

[ Run it ]  [ Open folder ]  [ Add tests ]
```

### Failure

```
❌ Build failed
CS1002: ; expected at Form1.cs:42

The generated Form1.cs has a syntax error on line 42.
Try fixing the semicolon or regenerating the file.

[ Show full errors ]  [ Retry ]  [ Fix automatically ]
```

### Single-Agent (no OrchestrationResult)

Falls back to existing TelegramUIAgent formatting of the raw response text.

## Website Documentation Updates

Four guide pages reference the old architecture and need updates:

### website/guide/telegram.md

- Replace all `IProject` references with `IThread`
- Replace `DelegateToAssistant → PersonalAssistant` with `Delegate → AgentSelector → CodeOrchestrator`
- Update architecture diagram to show: `Thread → AgentSelector → CodeOrchestrator → Agents`
- Update streaming throttle from 500ms to 1500ms
- Add section on progress updates during delegation
- Add section on structured result delivery with RichOutput

### website/guide/telegram-features.md

- Replace `IProject` references with `IThread`
- Replace `DelegateToAssistant` / `PersonalAssistant` with current delegation flow
- Update streaming rate from 500ms to 1500ms
- Update event streams table: remove dead events (`dashboard.changed`, `approval.requested`, `wizard.started`, `orchestration.completed`), update `orchestration.progress` description, add `job.completed`
- Update Task Delegation section to describe Thread → AgentSelector → CodeOrchestrator flow
- Add section on structured results and follow-up buttons

### website/guide/orchestration.md

- Replace `PlanningAgent` with `CodeOrchestratorAgent`
- Remove `OrchestrationPlan`, `PlanStep` references (no longer used)
- Remove `ScriptExecutor` section (CodeOrchestrator runs projects directly)
- Remove `CheckpointStore` section (not used in current architecture)
- Replace `PersonalAssistantAgent` with `ThreadAgent` + `AgentSelectorAgent`
- Update the full orchestration flow diagram to reflect: Thread → AgentSelector → CodeOrchestrator → generated C# → agent API calls
- Update `ScriptGenerator` section to reflect its current role (generates .csproj only)
- Add section on `OrchestrationResult` structured return type
- Add section on progress events published during orchestration
- Update agent registry references from `IAgentRegistryGrain` to `IAgentRegistry`
- Remove `AgentRegistration` record (replaced by `AgentRecord` + `AgentInterfaceMetadata`)

### website/guide/events-streams.md

- Update `PublishTypedAsync` references to `PublishToStream<T>`
- Remove `PersonalAssistantAgent` example in combined flow
- Update combined flow example to use current agent names (ThreadAgent, CodeOrchestrator)
- Verify stream name resolution table is still accurate

## What Does NOT Change

- TelegramUIAgent implementation (reused as-is)
- UISession callback handling
- Voice / photo / document upload paths
- Forum topics and `/start`, `/clear`, `/status` commands
- `notification.sent` stream handler
- Core Agent base class (except payload fix in Scheduling)
- RichOutput / UIPart type hierarchy
- DevUI (separate client, out of scope)

## Testing

- Unit test `OrchestrationResult` serialization/deserialization round-trip
- Unit test `SendProgressAsync` message tracking (first event creates, subsequent edits)
- Unit test `SendJobResultAsync` with structured result → TelegramUIAgent → RichOutput
- Unit test `SendJobResultAsync` fallback for plain string results
- Unit test payload key consistency in `Agent.Scheduling.OnScheduledJobDueAsync`
- Integration test: full delegation flow via Telegram → verify progress messages and final result
- Verify dead subscription removal doesn't break anything (no publishers exist, so no functional change)

## Migration

No breaking changes to external consumers. The `ICodeOrchestrator.ExecuteCodeOrchestration` signature change is internal — only called by ThreadAgent. The `OrchestrationResult` is a new type, not a replacement. Job result events remain on the same stream with the same name; the payload content changes from raw string to JSON-serialized `OrchestrationResult`, with fallback parsing for backward compatibility.
