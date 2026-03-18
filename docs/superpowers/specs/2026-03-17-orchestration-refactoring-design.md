# Orchestration Refactoring: Single Code Execution Path

**Date**: 2026-03-17
**Status**: Approved

## Problem

The current orchestration has two paths (`DelegateToAssistant` and `ExecuteWithCode`) with different failure modes. `DelegateToAssistant` chains LLMs together (Project → PersonalAssistant → sub-agent), accumulating tool results in conversation history until hitting the 200k token limit. `ExecuteWithCode` was meant to fix this but has Orleans method routing bugs and an overcomplicated CodeOrchestrator with unused task management, self-healing, and pause/resume features.

The codebase has 5 dead agent classes, 4 unused interfaces, and a `PersonalAssistant` that exists solely to route `DelegateToAssistant` calls to sub-agents.

## Solution

**One execution path.** Everything that requires action goes through generated C# code that calls agent grains directly. No LLM-to-LLM delegation. No PersonalAssistant router. No context accumulation.

The Project LLM is the brain. It decides:
- Answer directly (facts, memory, status)
- Execute immediately via code (clear intent: "build CalcEngine", "remind me in 30 mins")
- Ask user first, then execute (ambiguous scope: "refactor auth system")
- Show UI options (choices: "deploy to prod or staging?")

The LLM composes the interaction using its existing tools — `Execute`, `RequestApprovalTool`, `ScheduleJob`, or just text. Approval is NOT hardcoded into the execution path. It's the LLM's conversational judgment.

Simple task = simple generated code (10 lines calling one agent). Complex task = complex generated code (200 lines with loops and branching). The LLM decides the code complexity, not the routing path.

---

## Architecture

### The Flow

```
User sends message
    ↓
Project LLM evaluates using its tools:
    │
    ├─ Answer directly (knowledge, memory, status)
    │   → respond with text
    │
    ├─ Clear intent, low risk ("build CalcEngine", "remind me in 30 mins")
    │   → Execute(plan) immediately
    │   → result streamed back
    │
    ├─ Ambiguous or complex ("search Reddit for keyboards, make Excel")
    │   → respond with plan as text
    │   → RequestApprovalTool("Proceed with this plan?", ["Approve", "Decline"])
    │   → on Approve: Execute(plan)
    │   → on Decline: "What would you like to change?"
    │
    ├─ Needs user choice ("deploy to prod or staging?")
    │   → RequestApprovalTool(question, options)
    │   → on selection: Execute with the chosen option
    │
    └─ Simple scheduling ("remind me in 30 mins")
        → ScheduleJobTool directly, no code execution needed
```

The LLM is NOT told "always ask for approval" or "never ask for approval." It has tools and uses judgment. The instructions say: "If the user's request is clear and unambiguous, execute directly. If you need clarification or the task could be interpreted multiple ways, ask first."

### Project Grain Tools (after refactoring)

```csharp
protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(Execute, nameof(Execute),
        "Execute a task by generating and running C# code that calls agent interfaces"),
    AIFunctionFactory.Create(RequestApprovalTool, nameof(RequestApprovalTool),
        "Ask the user to choose between options or approve/decline something"),
    AIFunctionFactory.Create(RecallTool, nameof(RecallTool),
        "Search past task results and documents"),
    AIFunctionFactory.Create(ScheduleJobTool, nameof(ScheduleJobTool),
        "Schedule a recurring job"),
    // AddTask, UpdateTask, ListTasks, CancelJob, ListJobs stay
];
```

`DelegateToAssistant`, `ExecuteWithCode`, and `RequestExecution` are ALL deleted. Replaced by `Execute` + existing `RequestApprovalTool`.

### Execute Tool

```csharp
[Description("Execute a task by generating and running C# code. " +
    "The code connects to the agent cluster and calls agent interfaces directly. " +
    "Use for any task that requires action: building, file operations, " +
    "searching, data processing, code generation, etc.")]
private async Task<string> Execute(
    [Description("What to do, step by step")] string plan)
{
    var orchestrator = GrainFactory.GetGrain<IAgent>("code-orchestrator");
    var result = await orchestrator.GetResponse(plan, CancellationToken.None);
    return result;
}
```

Uses `IAgent.GetResponse` — no custom Orleans methods, no routing issues. The CodeOrchestrator grain receives the plan as a regular message and does its job.

### Project Grain Instructions

The instructions are critical. They must be concise, clear, and tell the LLM exactly how to behave. All topic variants follow this pattern:

```csharp
protected override string Instructions => GetTopicSlug() switch
{
    "general" => """
        You are the user's assistant. Be concise and direct.

        TOOLS:
        - Execute: run any task via generated code (build, create files, search, deploy, etc.)
        - RequestApprovalTool: ask user to choose or confirm before acting
        - Recall: search past results and documents
        - ScheduleJobTool: set up recurring tasks

        BEHAVIOR:
        - If you can answer from knowledge or memory, answer directly
        - If the request is clear ("build X", "check weather"), call Execute immediately
        - If the request is ambiguous or complex, explain your plan first,
          then use RequestApprovalTool to confirm before calling Execute
        - Never generate code in your response. Always use Execute.
        - Keep responses short. Use markdown formatting.
        """,
    // similar for "personal", "iaw", "scheduled", default
};
```

---

## CodeOrchestrator (simplified)

### Interface

```csharp
// Core.Contracts
public interface ICodeOrchestrator : IAgent;
```

No custom methods. Uses `GetResponse` inherited from `IAgent`. This avoids all Orleans method routing issues.

### Implementation

The CodeOrchestrator overrides `GetResponse` (now virtual on Agent base) to intercept calls and run the code generation pipeline:

```csharp
[GrainType("code-orchestrator-v1")]
public class CodeOrchestratorAgent(...) : Agent(...), ICodeOrchestrator
{
    public override async Task<string> GetResponse(string prompt, CancellationToken ct)
    {
        // 1. Read workspace path from env
        // 2. Create task folder
        // 3. Write plan.md
        // 4. Call ChatClient.GetResponseAsync to generate C# code
        //    (direct call, no Channel deadlock)
        // 5. Write orchestration.cs + orchestration.csproj
        // 6. dotnet run (out-of-process, inherits env vars)
        // 7. Capture stdout/stderr → log.txt
        // 8. Read result.json if exists
        // 9. Return compact result
    }
}
```

### Instructions (with InterfaceCatalog)

At grain activation, `InterfaceCatalog` discovers all agent interfaces via reflection. These are injected into the CodeOrchestrator's instructions so the LLM knows exactly what agents and methods it can call:

```
You generate standalone C# console applications.

The code must:
1. Be complete, compilable top-level statements
2. Use builder.AddIAWClient() to connect to the Orleans cluster
3. Call agent grain interfaces via GrainFactory
4. Write result.json at the end
5. Print progress to stdout
6. Wrap in try/catch, write errors to result.json

Available agents:
- IShell ("shell"): execute shell commands
  Methods: GetResponse(prompt) → runs command, returns output
- IFileSystem ("file-system"): file operations
  Methods: GetResponse(prompt) → read/write/search files
- IBuild ("build"): .NET build and test
- IGit ("git"): version control
- IReviewer ("reviewer"): code review
[... auto-generated from InterfaceCatalog ...]

Output ONLY C# code. No markdown fences. No explanation.
```

---

## Telegram Bot: Message Formatting

All bot messages use Telegram MarkdownV2 for clean, consistent UI.

### Execution result format

```
✅ *Build succeeded*

`dotnet build D:/CalcEngine` — 0 errors, 2 warnings
`dotnet test D:/CalcEngine` — 12/12 passed

_Completed in 8\.2s_
```

### Plan approval format (when LLM asks)

```
📋 *Plan*

1\. Search Reddit for keyboard threads
2\. Extract keyboard mentions and sentiment
3\. Generate Excel comparison
4\. Send file back

_3 agents involved • ~2 min estimated_
```
With inline buttons: `[✓ Approve]  [✗ Decline]`

### Error format

```
❌ *Build failed*

```
error CS1002: ; expected at Program\.cs:15
```

_Check D:/CalcEngine/Program\.cs line 15_
```

### Rules for all bot messages

- Maximum 4096 chars (Telegram limit) — truncate with `[…]` if needed
- Use MarkdownV2: `*bold*`, `_italic_`, `` `code` ``, ```` ```block``` ````
- Escape special chars: `_`, `*`, `[`, `]`, `(`, `)`, `~`, `` ` ``, `>`, `#`, `+`, `-`, `=`, `|`, `{`, `}`, `.`, `!`
- No walls of text. Short paragraphs, tables, bullet points.
- Result messages: status emoji + bold title + key metrics + duration
- Error messages: error emoji + bold title + relevant error excerpt + actionable hint

---

## What Gets Deleted

### Agents (delete entire files — 10 files)

```
src/Agents/Orchestration/PersonalAssistantAgent.cs
src/Agents/Orchestration/IPersonalAssistant.cs
src/Agents/Orchestration/PlanningAgent.cs
src/Agents/Orchestration/IPlanning.cs
src/Agents/Orchestration/TaskSupervisorAgent.cs
src/Agents/Orchestration/ITaskSupervisor.cs
src/Agents/Orchestration/DeployerAgent.cs
src/Agents/Orchestration/IDeployer.cs
src/Agents/Orchestration/NotificationAgent.cs
src/Agents/Orchestration/INotificationAgent.cs
```

### Core Orchestration (delete 6 files)

```
src/Core/Orchestration/CheckpointStore.cs
src/Core/Orchestration/OrchestrationPlan.cs
src/Core/Orchestration/StepRecord.cs
src/Core/Orchestration/StepResult.cs
src/Core/Orchestration/OrchestrationEvents.cs
src/Core/Orchestration/OrchestrationStatus.cs
```

### Simplify (3 files)

```
src/Core/Contracts/ICodeOrchestrator.cs → just ICodeOrchestrator : IAgent
src/Agents/Orchestration/CodeOrchestratorAgent.cs → simplified, override GetResponse
src/Core/Orchestration/ScriptGenerator.cs → keep .csproj template only
```

### Keep (2 files)

```
src/Core/Orchestration/ScriptExecutor.cs — runs dotnet processes
src/Core/Orchestration/InterfaceCatalog.cs — discovers agent interfaces via reflection
```

### Modify (4 files)

```
src/Agents/Projects/Project.cs
  - Delete: DelegateToAssistant, ExecuteWithCode
  - Add: Execute tool
  - Update: all Instructions variants (concise, tool-focused, markdown guidance)

src/Clients.Telegram/TelegramBotService.cs
  - Update: StreamResponseAsync to format results with MarkdownV2
  - Update: SendApprovalAsync to format plans with MarkdownV2
  - Add: exec: callback handler for plan approval flow

src/Clients.Telegram/StreamSubscriber.cs
  - Add: subscribe to execution.planned events (for when LLM uses approval flow)

src/Core/Agents/Agent.cs
  - Make GetResponse virtual (already done)
```

### Update (affected by PersonalAssistant deletion)

```
src/Agents/Memory/* — update MemoryContextProvider: remove PA memory agents if coupled
test/Core.Tests/* — delete tests for removed agents, add new tests
test/Integration.Tests/* — update orchestration tests
website/guide/* — update docs to reflect new architecture
```

---

## Context Management (unchanged)

- **L1**: Project history stays lean — Execute returns compact summary, never full output
- **L2**: ChatReducer token safety + message truncation (already implemented)
- **L3**: Task results in Qdrant via TaskResultContextProvider (already implemented)
- **Haiku summarization**: Applied to execution results before entering history

---

## Testing

### Unit Tests

1. **Execute tool calls CodeOrchestrator** — verify GetResponse called with plan
2. **CodeOrchestrator generates code and writes workspace files** — plan.md, .cs, .csproj, log.txt
3. **CodeOrchestrator handles execution failure** — error result returned
4. **InterfaceCatalog discovers agent interfaces** — IShell, IFileSystem, etc.
5. **ChatReducer still enforces token budget** — regression test

### Integration Tests

1. **Full flow: Execute → CodeOrchestrator → workspace files created**
2. **Approval flow: RequestApprovalTool → callback → Execute**
3. **Auto-execute flow: clear request → Execute called directly (no approval)**

### Manual Tests (via Telegram)

1. "build CalcEngine" → executes immediately, result with MarkdownV2
2. "compare Python and Go, make Excel" → plan shown, approve, then executes
3. "remind me about standup in 30 mins" → ScheduleJob, no code execution
4. "deploy to prod or staging?" → options shown, user picks, then executes
5. "what did we build yesterday?" → Recall tool, direct answer
