# Autonomous Code Orchestration with Self-Healing — Design Spec

## Problem

Users ask the assistant for complex, multi-step tasks ("research Reddit for the best mechanical keyboard of 2026 and create an Excel report with charts"). Today the Project agent can only respond with text — it can't autonomously decompose the task, execute multiple steps across specialized agents, recover from failures, or deliver rich artifacts (files, images) back to the user.

The orchestration infrastructure exists (ScriptGenerator, ScriptExecutor, InterfaceCatalog, PlanningAgent) but lacks: autonomous supervision, failure recovery, progress visibility, and result delivery.

## Goal

When a user sends a complex request via Telegram, the assistant autonomously:
1. Decomposes the task into an execution plan
2. Generates a typed C# orchestration script using discovered agent interfaces
3. Executes the script as a real .NET process
4. Monitors execution via Orleans streams with live progress updates to Telegram
5. On failure: the supervisor asks the LLM to adapt and retries — no user intervention
6. Delivers artifacts (Excel files, images, data) back to Telegram as documents/photos

## Architecture

```
User Request (Telegram)
    ↓
Project Agent → detects complex task → delegates to PlanningAgent
    ↓
PlanningAgent:
  1. InterfaceCatalog.Discover() — finds all available agent interfaces + capabilities
  2. LLM generates OrchestrationPlan (steps, agents, parameters)
  3. ScriptGenerator produces typed C# console app
  4. ScriptExecutor runs it as a .NET process
    ↓
Running Script:
  - Connects to Orleans cluster as client
  - Executes steps sequentially (or in parallel where safe)
  - Publishes progress events to "orchestration.progress" stream
  - Saves checkpoints to blob storage after each step
  - On failure: publishes error event to "orchestration.error" stream
    ↓
CodeOrchestratorAgent (Supervisor):
  - Subscribes to orchestration streams
  - Receives progress → forwards to Telegram as live updates
  - Receives error → sends error+context to LLM → gets fix → patches/regenerates script → retries
  - Receives completion → collects artifacts → delivers to user via Telegram
    ↓
Telegram Bot:
  - Renders progress updates as edited messages
  - Sends artifacts as documents/photos with summary
```

## Components

### 1. Supervisor (CodeOrchestratorAgent improvements)

The existing CodeOrchestratorAgent gets a supervision loop:

- Subscribes to `orchestration.progress` and `orchestration.error` Orleans streams
- Maintains task state: plan, current step, checkpoints, retry count
- On progress event: updates task state, publishes to Telegram notification stream
- On error event:
  1. Collects: failing step, exception, script context, checkpoint state
  2. Sends to LLM with prompt: "This step failed. Here's the error, the script, and the available agents. Generate a fix — either retry with different parameters, rewrite the step using an alternative agent, or skip if non-critical."
  3. LLM returns: action (retry/rewrite/skip) + modified code/parameters
  4. Supervisor applies the fix and re-executes from the checkpoint
  5. Max 3 self-healing attempts per step — after that, escalate to user via Telegram
- On completion: collects artifact paths from blob storage, triggers delivery

### 2. ScriptGenerator improvements

The generated scripts need to emit events and save checkpoints. New code patterns injected into generated scripts:

**Progress reporting:**
```csharp
await progressStream.OnNextAsync(new OrchestrationProgressEvent(
    taskId, stepIndex, "Searching web for mechanical keyboards...", DateTimeOffset.UtcNow));
```

**Checkpoint persistence:**
```csharp
await blobStorage.UploadAsync(
    JsonSerializer.SerializeToUtf8Bytes(searchResults),
    $"orchestration/{taskId}/step-{stepIndex}-results.json", "application/json");
```

**Error reporting (replaces silent try/catch):**
```csharp
try
{
    var results = await webSearch.SearchAsync(query);
}
catch (Exception ex)
{
    await errorStream.OnNextAsync(new OrchestrationErrorEvent(
        taskId, stepIndex, ex.GetType().Name, ex.Message, DateTimeOffset.UtcNow));
    return; // supervisor handles recovery
}
```

**Artifact registration:**
```csharp
var excelPath = $"orchestration/{taskId}/report.xlsx";
await blobStorage.UploadAsync(excelStream, excelPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
await artifactStream.OnNextAsync(new OrchestrationArtifactEvent(
    taskId, excelPath, "report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
```

### 3. ScriptExecutor improvements

- Stream stdout/stderr lines as events in real-time (instead of capturing at end)
- Pass taskId, Orleans gateway endpoint, and blob storage connection string as command-line args to the script
- Support script hot-patching: write a new Program.cs and re-run from checkpoint (for self-healing)

### 4. OrchestrationPlan improvements

Add to the plan model:

```csharp
public record OrchestrationPlan(
    string Summary,
    List<PlanStep> Steps,
    string TaskId,               // unique execution ID
    string ProjectId,            // project grain ID for context
    Dictionary<string, string> GlobalParameters);  // workspace, user prefs, etc.

public record PlanStep(
    int Order,
    string AgentType,            // e.g., "IWebSearch", "IFileSystem"
    string GrainId,              // e.g., "web-search"
    string Action,               // e.g., "SearchAsync"
    Dictionary<string, object> Parameters,
    bool Parallel,               // can run in parallel with next step?
    bool Critical);              // if false, failure can be skipped
```

### 5. Orleans Stream Events

New event types for orchestration communication:

```csharp
// Published by running scripts
public record OrchestrationProgressEvent(
    string TaskId, int StepIndex, string Message, DateTimeOffset Timestamp) : IEvent;

public record OrchestrationErrorEvent(
    string TaskId, int StepIndex, string ErrorType, string ErrorMessage,
    DateTimeOffset Timestamp) : IEvent;

public record OrchestrationArtifactEvent(
    string TaskId, string BlobPath, string FileName, string MimeType) : IEvent;

public record OrchestrationCompletedEvent(
    string TaskId, string Summary, List<string> ArtifactPaths,
    DateTimeOffset Timestamp) : IEvent;
```

### 6. Self-Healing Loop

When the supervisor receives an `OrchestrationErrorEvent`:

```
Error received
    ↓
Load: failing step, exception, full script, checkpoint state, available agents (InterfaceCatalog)
    ↓
LLM prompt:
  "Step {N} failed with {ErrorType}: {ErrorMessage}.
   The step was: {AgentType}.{Action}({Parameters}).
   Available agents: {catalog}.
   Checkpoint data from previous steps: {checkpointSummary}.

   Generate a fix. Options:
   1. Retry with modified parameters (return new parameters)
   2. Rewrite the step using a different agent (return new agent+action+params)
   3. Skip if non-critical (return skip)

   Return JSON: { action: 'retry'|'rewrite'|'skip', agentType?, action?, parameters? }"
    ↓
Apply fix:
  - retry: re-execute same step with new parameters
  - rewrite: generate new code for this step, insert into script, re-execute from checkpoint
  - skip: mark step as skipped, continue to next step
    ↓
If fix also fails (max 3 attempts): escalate to user via Telegram
  "Task step failed after 3 attempts: {error}. [Retry] [Skip] [Abort]"
```

### 7. Telegram Bot — Outgoing Media Support

Add to TelegramBotService:

```csharp
public async Task SendDocumentAsync(long chatId, Stream fileStream, string fileName, string? caption, int? topicId, CancellationToken ct)
{
    // Telegram Bot API sendDocument
}

public async Task SendPhotoAsync(long chatId, Stream photoStream, string? caption, int? topicId, CancellationToken ct)
{
    // Telegram Bot API sendPhoto
}
```

The StreamSubscriber subscribes to `orchestration.progress` and `orchestration.completed` streams:
- Progress → edit a pinned status message with current step
- Completed → download artifacts from blob storage, send as documents/photos with summary caption

### 8. Telegram Bot — Progress Display

When an orchestration starts, the bot sends a status message and edits it as progress events arrive:

```
🔄 Researching mechanical keyboards...
Step 1/4: Searching web... ✅
Step 2/4: Analyzing 47 results... ✅
Step 3/4: Generating Excel report... 🔄
Step 4/4: Creating chart...
```

On completion:
```
✅ Research complete!

📊 Found 47 keyboard recommendations across 12 Reddit threads.
Top pick: Keychron Q1 Max (mentioned 23 times)

📎 report.xlsx (47 keyboards, specs, prices, ratings)
📎 popularity-chart.png
```

### 9. MCP Tool Discovery

`InterfaceCatalog` currently discovers Orleans grain interfaces via reflection. Extend it to also discover MCP tools from the `.mcp.json` configuration:

- Parse `.mcp.json` to find configured MCP servers
- Query each server's tool list
- Include MCP tools in the catalog alongside grain interfaces
- ScriptGenerator can emit MCP tool calls alongside grain method calls

This allows the LLM to use Reddit MCP, Playwright MCP, or any other MCP server in orchestration plans without building dedicated Orleans agents for each.

## What's NOT in v1

- Specific specialized agents (IReddit, IExcel, IChart, IEmail) — plug-in additions built incrementally after the core framework
- Interactive checkpoints with user approval buttons mid-execution — escalation only on failure
- Browser automation agent
- Parallel step execution — sequential first, parallel optimization later
- Script caching/reuse — each task generates a fresh script

## File Changes Summary

| File | Change |
|------|--------|
| `src/Core/Orchestration/ScriptGenerator.cs` | Emit progress events, checkpoints, error reporting, artifact registration |
| `src/Core/Orchestration/ScriptExecutor.cs` | Stream stdout/stderr, support hot-patching, pass connection args |
| `src/Core/Orchestration/OrchestrationPlan.cs` | Add TaskId, ProjectId, GlobalParameters, step Parallel/Critical flags |
| `src/Core/Orchestration/InterfaceCatalog.cs` | Add MCP tool discovery from .mcp.json |
| `src/Core/Contracts/OrchestrationEvents.cs` | New: Progress, Error, Artifact, Completed event types |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | Supervisor loop: stream subscription, self-healing, escalation |
| `src/Agents/Orchestration/PlanningAgent.cs` | Integrate with supervisor, pass taskId/checkpoints |
| `src/Agents/Projects/Project.cs` | Detect complex tasks, delegate to PlanningAgent |
| `src/Clients.Telegram/TelegramBotService.cs` | SendDocumentAsync, SendPhotoAsync methods |
| `src/Clients.Telegram/StreamSubscriber.cs` | Subscribe to orchestration.progress, orchestration.completed streams |

## Success Criteria

1. User sends "research best mechanical keyboards 2026" in Telegram
2. Assistant decomposes into plan, generates script, starts execution
3. Telegram shows live progress updates as steps complete
4. If a step fails, supervisor self-heals (retry/rewrite/skip) without user input
5. On completion, Telegram receives artifact files + summary message
6. All of this works with any combination of Orleans grain agents and MCP tools
