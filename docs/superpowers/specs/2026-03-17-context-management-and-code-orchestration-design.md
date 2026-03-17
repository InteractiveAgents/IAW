# Context Management & Code Orchestration

**Date**: 2026-03-17
**Status**: Approved

## Problem Statement

Agents hit Anthropic's 200k token limit when orchestrating complex multi-step tasks. The current architecture uses LLM-to-LLM delegation (`DelegateToAssistant` → `PersonalAssistant` → sub-agents), where full tool results (build output, code reviews, research data) accumulate in the orchestrating agent's conversation history. A single delegation chain can produce 50k+ tokens of output, and after a few interactions the history exceeds the model's context window.

The root cause is architectural: the orchestration layer uses LLM conversation as both the control plane AND the data plane. Everything flows through natural language in conversation history.

## Solution Overview

Two complementary changes:

1. **Code Orchestration (Mode 2)**: For complex tasks, an LLM plans the work and a `CodeOrchestrator` agent generates standalone C# code that calls agent interfaces directly. The code handles iteration, aggregation, and file generation — things LLMs are bad at. No context accumulation because the orchestration layer is code, not conversation.

2. **Tiered Context Management**: For all interactions, a three-tier memory hierarchy (L1: active context, L2: compacted history, L3: vector store) keeps the LLM's context window lean while preserving full knowledge in searchable storage.

---

## Part 1: Dual Orchestration Modes

### Mode 1: LLM Delegation (existing, enhanced)

For simple, single-agent tasks: "build this project", "review that file", "check git status".

Flow: Project LLM → `DelegateToAssistant` → PersonalAssistant → sub-agent → result

**Enhancement**: Sub-agent results get summarized via a Haiku LLM call before returning to the orchestrator's context. The full output is already streamed to the user via `WriteToolProgress` and stored in the vector store. Only the compact summary enters the conversation history.

Cost: ~$0.001 per delegation, ~1s latency.

### Mode 2: Code Orchestration (new)

For complex multi-step tasks involving loops, data processing, multi-source research, or file generation.

Flow:
1. Project LLM understands intent, defines success metrics, generates pseudocode plan
2. `ExecuteWithCode` tool passes the plan to `CodeOrchestrator` agent
3. CodeOrchestrator's LLM generates a standalone C# file that calls IAW agent interfaces via `Aspire.IAW.Client`
4. Code is written to disk, compiled, and executed out-of-process via `dotnet run`
5. The generated process inherits the silo's environment variables — `AddIAWClient()` connects to the cluster automatically
6. Progress streams to user via `WriteToolProgress`
7. Compact result summary returned to Project LLM

### When to use which

| Signal | Route |
|--------|-------|
| Quick question, status check, casual chat | Direct answer (no delegation) |
| Single-agent task (build, review, git op) | Mode 1: `DelegateToAssistant` |
| Multi-step work, loops, data processing, file generation, multi-source research | Mode 2: `ExecuteWithCode` |

The Project grain's instructions tell the LLM when to use which tool. No separate Planner agent — the Project grain IS the planner.

---

## Part 2: CodeOrchestrator Agent

### Interface

```csharp
public interface ICodeOrchestrator : IAgent
{
    // Inherits GetResponseStream from IAgent
}
```

### Grain: `CodeOrchestratorAgent`

An Orleans grain that:
1. Receives a plan (intent + success metrics + pseudocode steps)
2. Generates a standalone C# file using its LLM, referencing `Aspire.IAW.Client` and all agent interfaces
3. Writes the file + auto-generated .csproj to the workspace directory
4. Executes via `dotnet run --project {taskFolder}` as an out-of-process child
5. The child process inherits the parent's environment (Orleans clustering config, API keys, etc.) so `AddIAWClient()` works automatically
6. Captures stdout/stderr to `log.txt`
7. Reads `result.json` (written by the generated code) for structured output
8. Returns a compact summary to the caller

### Workspace Directory Structure

Configured via `.WithWorkspace(path)` in Aspire AppHost. Propagated as `IAW__Workspace` env var.

```
D:\IAW-Workspace\
  tasks\
    2026-03-17-keyboard-comparison-a1b2c3\
      plan.md              <- the plan that generated this code
      orchestration.cs     <- generated C# code
      orchestration.csproj <- auto-generated project file
      log.txt              <- stdout/stderr capture
      result.json          <- structured result from the code
      output\              <- result artifacts (Excel files, etc.)
    2026-03-17-build-calcengine-d4e5f6\
      ...
  templates\
    orchestration.csproj.template  <- shared project template
```

Naming: `{date}-{slug}-{shortId}`. Slug derived from plan intent. ShortId for uniqueness.

### Generated .csproj (template)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net11.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.IAW.Client" />
  </ItemGroup>
</Project>
```

### result.json (written by generated code)

```json
{
  "status": "success",
  "summary": "Generated comparison of 14 keyboards from 20 Reddit threads",
  "artifacts": ["output/keyboard-comparison.xlsx"],
  "metrics": {
    "keyboards_found": 14,
    "threads_analyzed": 20,
    "execution_time_seconds": 45
  }
}
```

If the process crashes before writing `result.json`, the CodeOrchestrator reads stderr from `log.txt` and reports failure.

### Execution & Error Handling

- `dotnet run` gets a configurable timeout (default: 10 minutes)
- Process exit non-zero -> read `log.txt`, return error summary
- Timeout -> kill process, return "Task timed out after 10 minutes"
- Generated code wraps main logic in try/catch, writes errors to `result.json`

### ExecuteWithCode tool on Project grain

```csharp
[Description("Execute a complex task via generated C# code. Use for tasks involving " +
    "loops, data processing, multi-source research, file generation, or multi-step workflows. " +
    "Provide: what the user wants, success metrics (what columns/data points), and step-by-step plan.")]
private async Task<string> ExecuteWithCode(string plan)
{
    var orchestrator = GrainFactory.GetGrain<ICodeOrchestrator>("code-orchestrator");
    var sb = new StringBuilder();
    WriteToolProgress("\n\n---\nGenerating and executing code...\n\n");
    await foreach (var chunk in orchestrator.GetResponseStream(plan, CancellationToken.None))
    {
        sb.Append(chunk);
        WriteToolProgress(chunk);
    }
    WriteToolProgress("\n---\n");
    return sb.ToString();
}
```

---

## Part 3: Tiered Context Management

### L1: Active Context (goes to LLM)

What the LLM sees on each call:
- System instructions (with topic-aware context)
- Context providers output (RAG, memory agents, user context, task context)
- Recent conversation history (recent window from ChatReducer)
- Summary of older history (from HistorySummarizer)

**Mode 1 enhancement**: Tool results summarized via Haiku before entering history. The `AssignTaskToAgent` wrapper in PersonalAssistantAgent takes the full sub-agent response, calls a Haiku summarizer, and returns the compact summary as the tool result. Full output already streamed to user + stored in Qdrant.

**Mode 2**: CodeOrchestrator returns only a compact result (e.g., "Executed task. Generated file.xlsx with 14 items. Sent to Telegram."). Full execution log on disk + in Qdrant. Never enters Project's history.

**Safety net**: `ChatReducer` gets a token estimation check. Before returning the message list, estimate total chars (~4 chars/token). If over budget (400k chars ~ 100k tokens), drop oldest non-summary messages. Last resort, not primary strategy.

### L2: Durable History (Orleans journaled state)

Full conversation history stays in `IDurableList<ChatMessage>`. The `ChatReducer` applies two reductions:

**Existing**: Recent window (20 messages verbatim) + summary of older messages + non-reducible pinned messages.

**New -- Post-task compaction**: `ChatReducer` detects completed tool exchanges in the older portion of history (assistant message with tool call -> tool result -> assistant follow-up) and collapses them into a single summary message:

Before compaction:
```
[user] Build the CalcEngine project
[assistant] (tool call to DelegateToAssistant)
[tool result] 50k chars of build output
[assistant] Done! Here's what happened...
```

After compaction:
```
[user] Build the CalcEngine project
[assistant] [Completed task] Created CalcEngine with MathEngine class (4 methods), xUnit tests (12 pass), committed abc123.
```

Compaction only applies to messages that have aged out of the recent window. Recent exchanges stay verbatim.

### L3: Vector Store (Qdrant -- long-term searchable memory)

**Existing collections**:
- `project-{projectId}`: Per-project document chunks (PDF uploads)
- `iaw-user-memory`: User facts and preferences
- `iaw-project-memory`: Project conventions and decisions
- `iaw-episode-memory`: Task workflows and outcomes
- `iaw-code-memory`: Code structure and dependencies
- `iaw-pattern-memory`: Design patterns and recommendations

**New collection**:
- `task-results-{userId}`: Embedded task results from both Mode 1 (summarized delegation results) and Mode 2 (CodeOrchestrator execution summaries + plans)

**What gets embedded**:
- Mode 1: Full sub-agent response (before summarization) -> chunked and embedded
- Mode 2: Plan + result summary + success metrics -> embedded
- Episode memory: Already observes task completions (unchanged)

### Agent Recall Tool (new)

Available on the Project grain:

```csharp
[Description("Search past task results, conversations, and documents for relevant context")]
private async Task<string> Recall(string query, int maxResults = 5)
```

Searches:
1. `task-results-{userId}` collection (past task outputs)
2. `project-{projectId}` collection (uploaded documents)
3. Episode memory agent

Returns top results as formatted context. This means "what keyboard did we find was best last time?" works even after the full analysis was compacted out of L1 history.

### Data Flow

```
User sends message
    |
    v
Project grain -> BuildContextBlock():
    L3 retrieval: RAG + Memory agents + task results from Qdrant
    |
    v
LLM decides route:
    |-- Direct answer -> respond (no context issue)
    |-- Mode 1: DelegateToAssistant
    |   |-- Full output -> WriteToolProgress -> user sees everything
    |   |-- Full output -> embedded in Qdrant (L3)
    |   |-- Haiku summarizes -> compact summary returned to LLM (L1)
    |   \-- Later: ChatReducer compacts the exchange in history (L2)
    \-- Mode 2: ExecuteWithCode
        |-- Plan -> CodeOrchestrator -> generates C# -> executes
        |-- Progress -> WriteToolProgress -> user sees everything
        |-- Full log + artifacts on disk (workspace)
        |-- Summary + plan embedded in Qdrant (L3)
        |-- Compact result returned to Project LLM (L1)
        \-- History stays clean: just user request + outcome (L2)
```

---

## Part 4: Aspire Integration

### `.WithWorkspace(path)` on IAWService

```csharp
// In AppHost:
builder.AddIAW("iaw")
    .WithWorkspace("D:\\IAW-Workspace")
```

Propagates as `IAW__Workspace` environment variable to all services via `.WithReference(iaw)`.

### CodeOrchestrator reads workspace path

At grain activation, reads `IAW__Workspace` from environment. Falls back to a temp directory if not configured.

### Environment inheritance for child processes

The CodeOrchestrator starts `dotnet run` with `Process.Start`. The child process inherits the silo's environment by default — this includes:
- `Orleans__ClusterId`, `Orleans__Clustering__ProviderType`, gateway endpoints
- `AI__LLM__*` API keys and model config
- `ConnectionStrings__*` for Qdrant, blob storage
- Everything the `AddIAWClient()` call needs to connect

No additional configuration needed.

---

## Files Changed/Created

### New files

| File | Purpose |
|------|---------|
| `src/Core/Contracts/ICodeOrchestrator.cs` | Grain interface (extends IAgent) |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | CodeOrchestrator grain implementation |
| `src/Core/Context/TaskResultContextProvider.cs` | L3 retrieval of past task results from Qdrant |

### Modified files

| File | Change |
|------|--------|
| `src/Agents/Projects/Project.cs` | Add `ExecuteWithCode` tool, add `Recall` tool, update instructions for Mode 1 vs Mode 2 routing |
| `src/Agents/Orchestration/PersonalAssistantAgent.cs` | Haiku summarization in `AssignTaskToAgent` return value |
| `src/Core/Agents/ChatReducer.cs` | Token estimation safety net, post-task compaction of completed tool exchanges |
| `src/Core/Agents/DurableChatHistoryProvider.cs` | Pass token budget to reducer |
| `src/Aspire.Hosting.IAW/IAWService.cs` | Add `.WithWorkspace(path)` fluent method |
| `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs` | Propagate `IAW__Workspace` env var |

### Website documentation updates

| File | Change |
|------|--------|
| `website/guide/architecture.md` | Add tiered context management section, dual orchestration modes |
| `website/guide/agents.md` | Document CodeOrchestrator agent |
| `website/guide/behaviors/conversation.md` | Update conversation management with L1/L2/L3 tiers, compaction |

---

## Testing

### Automated Tests

1. **ChatReducer token safety net**: Create history with messages totaling >400k chars. Verify reducer trims to under budget.
2. **ChatReducer post-task compaction**: Create history with completed tool exchange pattern. Verify it collapses to single summary.
3. **Haiku summarization in AssignTaskToAgent**: Verify long results get summarized before return.
4. **CodeOrchestrator**: Generate a simple C# file, execute, verify result.json is read correctly.
5. **Recall tool**: Embed task results in Qdrant, verify search returns relevant results.

### Manual Tests

- Send complex task in Telegram -> verify Mode 2 triggers, code generates and executes
- Send simple task -> verify Mode 1 still works with summarization
- After many interactions, verify history stays within token budget
- Use Recall to search past task results
- Inspect workspace directory for generated files

---

## Known Limitations

1. **CodeOrchestrator needs NuGet restore**: First execution per task folder requires `dotnet restore` which adds ~5-10s. Subsequent runs are faster. Could pre-cache packages.
2. **Generated code quality**: LLM-generated C# may have bugs. The code runs in try/catch with error reporting, but may need retry logic.
3. **No sandboxing**: Generated code runs with full silo permissions. Acceptable for single-user dev scenarios; needs sandboxing for multi-tenant.
4. **Haiku summarizer latency**: Adds ~1s per Mode 1 delegation. Acceptable trade-off vs 200k token crash.
5. **Token estimation is approximate**: ~4 chars/token is a rough heuristic. Could under/over-estimate for non-English or code-heavy content.
