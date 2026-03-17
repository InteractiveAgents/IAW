# Orchestration Refactoring: Plan → Approve → Execute

**Date**: 2026-03-17
**Status**: Approved

## Problem

The current orchestration has two paths (`DelegateToAssistant` and `ExecuteWithCode`) with different failure modes. `DelegateToAssistant` chains LLMs together (Project → PersonalAssistant → sub-agent), accumulating tool results in conversation history until hitting the 200k token limit. `ExecuteWithCode` was meant to fix this but has Orleans method routing bugs and an overcomplicated CodeOrchestrator with unused task management, self-healing, and pause/resume features.

The codebase has 5 dead agent classes, 4 unused interfaces, and a `PersonalAssistant` that exists solely to route `DelegateToAssistant` calls to sub-agents.

## Solution

**One orchestration path.** Everything that requires action goes through: Plan → User Approval → Generated C# Code → Execute.

No LLM-to-LLM delegation. No PersonalAssistant router. No context accumulation. The Project LLM decides what to do, writes a short plan, the user approves, and a generated C# app does the actual work by calling agent grains directly.

Simple task = simple generated code (10 lines). Complex task = complex generated code (200 lines with loops). The LLM controls the complexity of the CODE, not the routing path.

---

## Architecture

### The Flow

```
User sends message
    ↓
Project LLM evaluates:
    ├─ Can answer directly (facts, status, memory) → respond
    └─ Needs action (build, create file, search, generate, deploy) →
        ↓
        1. LLM writes short plan (3-5 bullets)
        2. Plan sent to Telegram with [Approve] [Decline] buttons
        3. User taps Approve → CodeOrchestrator.Execute(plan)
           User taps Decline → "What would you like to change?"
        4. CodeOrchestrator:
           a. LLM generates standalone C# file
           b. Uses InterfaceCatalog to discover available agent interfaces
           c. Writes .cs + .csproj to workspace
           d. dotnet run (out-of-process, inherits env vars)
           e. Captures output, reads result.json
        5. Result sent back to Telegram
        6. Compact summary stored in Project history (not full output)
```

### Project Grain Tools (after refactoring)

```csharp
protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(RequestExecution, nameof(RequestExecution),
        "Present a plan to the user for approval, then execute via generated C# code"),
    AIFunctionFactory.Create(RecallTool, nameof(RecallTool),
        "Search past task results and documents"),
    AIFunctionFactory.Create(RequestApprovalTool, nameof(RequestApprovalTool),
        "Ask the user a question with options"),
    // Task management tools (AddTask, UpdateTask, ListTasks, ScheduleJob, etc.) stay
];
```

`DelegateToAssistant` and `ExecuteWithCode` are both deleted. Replaced by single `RequestExecution`.

### RequestExecution Tool

```csharp
[Description("Present a plan to the user and request approval to execute it. " +
    "The plan will be shown in Telegram with Approve/Decline buttons. " +
    "On approval, a C# program is generated and executed to carry out the plan.")]
private async Task<string> RequestExecution(
    [Description("Short plan: what you'll do, step by step (3-5 bullets)")] string plan)
{
    var executionId = Guid.NewGuid().ToString("N")[..8];
    // Store plan in durable state for retrieval on approval
    State[$"execution-{executionId}"] = new StateEntry($"execution-{executionId}", plan);
    await WriteStateAsync(CancellationToken.None);

    // Publish approval request with the plan
    await PublishAsync("execution.planned", new Dictionary<string, object>
    {
        ["executionId"] = executionId,
        ["plan"] = plan,
        ["projectSlug"] = this.GetPrimaryKeyString()
    });

    return $"Plan submitted for approval (id: {executionId}). Waiting for user to approve or decline.";
}
```

### Telegram Approval Flow

The `StreamSubscriber` subscribes to `execution.planned` events. The bot sends the plan to the user's topic with two buttons:

```
Plan:
1. Run dotnet build D:/CalcEngine
2. Run dotnet test D:/CalcEngine
3. Report results

[✓ Approve]  [✗ Decline]
```

**On Approve**: Telegram callback handler calls `CodeOrchestrator.Execute(plan)`, streams result back.

**On Decline**: Bot sends "What would you like to change?" — user provides clarification, which goes back to the Project LLM as a new message.

### CodeOrchestrator (simplified)

```csharp
// Core.Contracts
public interface ICodeOrchestrator : IAgent
{
    // No custom methods — uses GetResponse inherited from IAgent
}
```

The CodeOrchestrator overrides behavior through its `Instructions` and `DefineTools()`. When it receives a plan via `GetResponse`, it:

1. Generates C# code using its LLM (calls `ChatClient.GetResponseAsync` directly to avoid Channel deadlock)
2. Writes files to workspace
3. Executes via `dotnet run`
4. Returns result

No `ExecuteCodeOrchestration` method. No Orleans interface routing issues. Just `IAgent.GetResponse` which works everywhere.

The CodeOrchestrator's instructions tell the LLM exactly how to generate code:
- Use `Aspire.IAW.Client` and `AddIAWClient()` for cluster connection
- Available agent interfaces (discovered via `InterfaceCatalog` at activation, injected into instructions)
- Always write `result.json`
- Always wrap in try/catch
- Print progress to stdout

### InterfaceCatalog (kept, enhanced)

Already exists in `Core.Orchestration`. Uses reflection to discover all `IAgent` interfaces and their methods. At CodeOrchestrator grain activation, the catalog runs and the discovered interfaces are embedded into the LLM instructions. This way the LLM knows exactly what agents and methods are available.

### Workspace (unchanged)

Configured via `.WithWorkspace(path)` in Aspire. Scripts stored at `{workspace}/tasks/{date}-{slug}-{id}/`.

---

## What Gets Deleted

### Agents (delete entire files)

| File | Reason |
|------|--------|
| `src/Agents/Orchestration/PersonalAssistantAgent.cs` | Replaced by code orchestration |
| `src/Agents/Orchestration/IPersonalAssistant.cs` | No longer needed |
| `src/Agents/Orchestration/PlanningAgent.cs` | Dead code, never shipped |
| `src/Agents/Orchestration/IPlanning.cs` | Dead code |
| `src/Agents/Orchestration/TaskSupervisorAgent.cs` | Dead code, never shipped |
| `src/Agents/Orchestration/ITaskSupervisor.cs` | Dead code |
| `src/Agents/Orchestration/DeployerAgent.cs` | Dead code, never shipped |
| `src/Agents/Orchestration/IDeployer.cs` | Dead code |
| `src/Agents/Orchestration/NotificationAgent.cs` | Dead code, never shipped |
| `src/Agents/Orchestration/INotificationAgent.cs` | Dead code |

### Core Orchestration (cleanup)

| File | Action |
|------|--------|
| `src/Core/Orchestration/CheckpointStore.cs` | DELETE — never used |
| `src/Core/Orchestration/OrchestrationPlan.cs` | DELETE — replaced by plain string plans |
| `src/Core/Orchestration/StepRecord.cs` | DELETE — no more step tracking |
| `src/Core/Orchestration/StepResult.cs` | DELETE — no more step tracking |
| `src/Core/Orchestration/OrchestrationEvents.cs` | DELETE — replaced by simpler events |
| `src/Core/Orchestration/OrchestrationStatus.cs` | DELETE — no more status enum |
| `src/Core/Orchestration/ScriptGenerator.cs` | KEEP or simplify — generates .csproj template |
| `src/Core/Orchestration/ScriptExecutor.cs` | KEEP — runs dotnet processes |
| `src/Core/Orchestration/InterfaceCatalog.cs` | KEEP — discovers agent interfaces via reflection |

### Contracts

| File | Action |
|------|--------|
| `src/Core/Contracts/ICodeOrchestrator.cs` | SIMPLIFY — just `ICodeOrchestrator : IAgent` (no custom methods) |

### Project.cs Changes

| What | Action |
|------|--------|
| `DelegateToAssistant` tool | DELETE |
| `ExecuteWithCode` tool | DELETE |
| `RequestExecution` tool | ADD (new) |
| Instructions | UPDATE — two modes: answer directly or RequestExecution |

### Tests

| What | Action |
|------|--------|
| Tests for deleted agents | DELETE |
| Tests for deleted orchestration types | DELETE |
| New test: RequestExecution flow | ADD |
| New test: CodeOrchestrator.GetResponse generates + executes code | ADD |

---

## Telegram Bot Changes

### New stream subscription: `execution.planned`

```csharp
var executionStream = streamProvider.GetStream<AgentEvent>(
    StreamId.Create(IAWConstants.StreamProvider, "execution.planned"));
await executionStream.SubscribeAsync(async (evt, token) =>
{
    var executionId = evt.Payload["executionId"]?.ToString() ?? "";
    var plan = evt.Payload["plan"]?.ToString() ?? "";
    var projectSlug = evt.Payload["projectSlug"]?.ToString() ?? "";
    await botService.SendExecutionApprovalAsync(executionId, plan, projectSlug, ct);
});
```

### New method: `SendExecutionApprovalAsync`

Sends the plan text with two inline buttons:
- `exec:{executionId}:approve` → triggers execution
- `exec:{executionId}:decline` → asks for clarification

### Callback handler for `exec:` prefix

In `HandleCallbackQueryAsync`, handle `exec:` callbacks:
- **Approve**: Get the plan from the Project grain's state, call `CodeOrchestrator.GetResponse(plan)`, stream result back to the topic
- **Decline**: Send "What would you like to change?" in the topic

---

## Context Management Integration

The tiered context management from the earlier spec still applies:

- **L1**: Project history stays lean — only the plan + result summary, never full execution output
- **L2**: ChatReducer token safety net + message truncation (already implemented)
- **L3**: Task results embedded in Qdrant via TaskResultContextProvider (already implemented)
- **Haiku summarization**: Applied to the CodeOrchestrator result before it enters Project history

---

## Files Changed/Created Summary

### Delete (14 files)

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
src/Agents/Orchestration/CodeOrchestratorAgent.cs → simplified Execute logic
src/Core/Orchestration/ScriptGenerator.cs → keep .csproj template generation
```

### Modify (3 files)

```
src/Agents/Projects/Project.cs → remove DelegateToAssistant/ExecuteWithCode, add RequestExecution
src/Clients.Telegram/TelegramBotService.cs → add SendExecutionApprovalAsync
src/Clients.Telegram/StreamSubscriber.cs → subscribe to execution.planned
```

### Update (affected by PersonalAssistant deletion)

```
src/Agents/Memory/* → remove MemoryContextProvider references to PA if any
test/Core.Tests/* → delete tests for removed agents, add new tests
test/Integration.Tests/* → update orchestration tests
```

---

## Testing

### Unit Tests

1. **RequestExecution stores plan and publishes event** — verify state entry created, event published
2. **CodeOrchestrator generates code and writes files** — verify workspace, plan.md, .cs, .csproj, log.txt
3. **CodeOrchestrator handles execution failure** — verify error result returned
4. **InterfaceCatalog discovers agent interfaces** — verify IShell, IFileSystem, etc. found

### Integration Tests

1. **Full flow: plan → approve callback → execute → result** — end-to-end with TestCluster
2. **Decline flow: plan → decline → clarification message**

### Manual Tests (via Telegram)

1. Send "build CalcEngine" → plan appears with buttons → tap Approve → result streams back
2. Send "compare Python and Go, make Excel" → plan appears → tap Approve → code generates, executes
3. Send complex task → tap Decline → provide clarification → new plan appears
