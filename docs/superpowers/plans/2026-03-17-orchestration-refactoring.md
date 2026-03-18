# Orchestration Refactoring Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace dual orchestration (DelegateToAssistant + ExecuteWithCode) with a single code-execution path, delete 16 dead files, and simplify the CodeOrchestrator.

**Architecture:** Project LLM uses `Execute(plan)` tool → CodeOrchestrator generates C# → `dotnet run` out-of-process. Approval is the LLM's judgment via existing `RequestApprovalTool`. PersonalAssistant and 9 other dead agents are deleted.

**Tech Stack:** C# / .NET 11, Orleans 10, Telegram.BotAPI 9.5.0, xUnit v3

**Spec:** `docs/superpowers/specs/2026-03-17-orchestration-refactoring-design.md`

---

## Chunk 1: Delete Dead Code

### Task 1: Delete dead agent files

**Files to delete:**
```
src/Agents/Orchestration/PlanningAgent.cs
src/Agents/Orchestration/IPlanning.cs
src/Agents/Orchestration/TaskSupervisorAgent.cs
src/Agents/Orchestration/ITaskSupervisor.cs
src/Agents/Orchestration/DeployerAgent.cs
src/Agents/Orchestration/IDeployer.cs
src/Agents/Orchestration/NotificationAgent.cs
src/Agents/Orchestration/INotificationAgent.cs
src/Agents/Orchestration/PersonalAssistantAgent.cs
src/Agents/Orchestration/IPersonalAssistant.cs
```

- [ ] **Step 1: Delete the 10 agent files**

```bash
cd E:/IAW
rm src/Agents/Orchestration/PlanningAgent.cs
rm src/Agents/Orchestration/IPlanning.cs
rm src/Agents/Orchestration/TaskSupervisorAgent.cs
rm src/Agents/Orchestration/ITaskSupervisor.cs
rm src/Agents/Orchestration/DeployerAgent.cs
rm src/Agents/Orchestration/IDeployer.cs
rm src/Agents/Orchestration/NotificationAgent.cs
rm src/Agents/Orchestration/INotificationAgent.cs
rm src/Agents/Orchestration/PersonalAssistantAgent.cs
rm src/Agents/Orchestration/IPersonalAssistant.cs
```

- [ ] **Step 2: Delete dead orchestration types**

```bash
rm src/Core/Orchestration/CheckpointStore.cs
rm src/Core/Orchestration/OrchestrationPlan.cs
rm src/Core/Orchestration/StepRecord.cs
rm src/Core/Orchestration/StepResult.cs
rm src/Core/Orchestration/OrchestrationEvents.cs
rm src/Core/Orchestration/OrchestrationStatus.cs
```

- [ ] **Step 3: Delete tests for deleted code**

```bash
rm test/Core.Tests/TaskSupervisorTests.cs
rm test/Core.Tests/Orchestration/OrchestrationPlanTests.cs
rm test/Core.Tests/Orchestration/OrchestrationTypesTests.cs
```

Also update `test/Core.Tests/ArchitectureGuardV2Tests.cs` — remove any assertions about deleted agents/types. Read the file first to understand what needs changing.

Update `test/Integration.Tests/OrchestrationIntegrationTests.cs` — remove tests that reference deleted types (ICodeOrchestrator.CreateTask, PauseTask, etc.). Keep tests for basic agent functionality if they still apply.

- [ ] **Step 4: Fix compile errors from deletions**

The following files reference deleted types and need updating:

**`src/Agents/Orchestration/CodeOrchestratorAgent.cs`**: Remove `using Core.Orchestration;` and all methods that reference deleted types (`CreateTask`, `GetTaskState`, `PauseTask`, `ResumeTask`, `ExecuteOrchestration`, `SelfHealAsync`, `UpdateTaskStatus`, `PublishProgressAsync`, `PublishCompletedAsync`, `ParseError`). Also remove the `TaskPrefix`, `MaxSelfHealAttempts` constants. Keep only: `ExecuteCodeOrchestration`, `GenerateCode`, `GenerateCsproj`, `ExecuteProject`, `GenerateSlug`.

**`src/Core/Orchestration/ScriptGenerator.cs`**: This references `OrchestrationPlan` and `PlanStep`. Simplify to keep only the `.csproj` template generation method. Delete the `Generate(plan, host, port)` method.

**`src/Core/Contracts/ICodeOrchestrator.cs`**: Simplify to just:
```csharp
namespace Core.Contracts;

public interface ICodeOrchestrator : IAgent;
```
Delete `TaskState` record and all method declarations.

**`src/Agents/Projects/Project.cs`**: Remove `DelegateToAssistant` and `ExecuteWithCode` methods. Remove them from `DefineTools()`. Remove `using IAW.Agents.Orchestration;` if it was only for `IPersonalAssistant`. Will be fully updated in Task 3.

**`src/IAW.MCP/Tools/AgentTools.cs`**: The `AssistantChat` method (line 62-71) references `personal-assistant`. Change it to use the `project` agent instead:
```csharp
[McpServerTool(Name = "assistant_chat")]
[Description("Send a message to the project assistant and get a response.")]
public async Task<string> AssistantChat(
    [Description("The message to send")] string message,
    [Description("Project ID (default: general)")] string projectId = "general",
    CancellationToken ct = default)
{
    var agent = ResolveAgent(projectId);
    var response = await agent.GetResponse(message, ct);
    return JsonSerializer.Serialize(new { agentId = projectId, response }, JsonOptions);
}
```

**`test/Core.Tests/CodeOrchestratorTests.cs`**: Remove tests that reference deleted methods (`CreateTask_returns_task_id`, `GetTaskState_returns_created_status`, `PauseTask_updates_status`, `ResumeTask_after_pause_sets_running`). Keep `CodeOrchestrator_metadata_correct`, `ExecuteCodeOrchestration_CreatesWorkspaceFiles`, `ExecuteCodeOrchestration_ReturnsErrorOnBadPath`.

- [ ] **Step 5: Build**

Run: `dotnet build src/Core && dotnet build src/Agents`
Expected: Build succeeded

If MCP or DevUI fail, fix the references. DevUI's `AgentDiscovery.cs` auto-discovers — deleting interfaces just removes them. No code changes needed.

- [ ] **Step 6: Run tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All remaining tests pass

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: delete 16 dead orchestration files and PersonalAssistant

Removed: PlanningAgent, TaskSupervisor, Deployer, Notification agents
Removed: OrchestrationPlan, StepRecord, CheckpointStore, OrchestrationStatus
Removed: PersonalAssistant (replaced by code orchestration)
Simplified: ICodeOrchestrator, CodeOrchestratorAgent, ScriptGenerator"
```

---

## Chunk 2: Simplify CodeOrchestrator

### Task 2: Rewrite CodeOrchestratorAgent with override GetResponse

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`
- Modify: `src/Core/Orchestration/ScriptGenerator.cs`

- [ ] **Step 1: Rewrite CodeOrchestratorAgent**

Replace the entire file with a clean implementation. The agent overrides `GetResponse` (virtual on Agent base class) to run the code generation + execution pipeline:

```csharp
using System.Diagnostics;
using System.Text;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Orchestration;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Orchestration;

[GrainType("code-orchestrator-v1")]
public class CodeOrchestratorAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient), ICodeOrchestrator
{
    static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(10);

    protected override string DisplayName => "Code Orchestrator";

    protected override string Instructions
    {
        get
        {
            var catalog = InterfaceCatalog.Discover();
            return $"""
                You generate standalone C# console applications that execute task plans.

                The code must:
                1. Use top-level statements
                2. Call builder.AddIAWClient() to connect to the Orleans cluster
                3. Get agents via host.Services.GetRequiredService<IClusterClient>().GetGrain<IAgent>("grain-id")
                4. Call GetResponse(prompt) on agents to delegate work
                5. Write result.json with: status, summary, artifacts[], metrics{{}}
                6. Write output files to "output/" subdirectory
                7. Wrap main logic in try/catch, write errors to result.json
                8. Print progress to stdout

                Available agents:
                {catalog}

                Output ONLY C# code. No markdown. No explanation.
                """;
        }
    }

    protected override IReadOnlyList<AITool> DefineTools() => [];

    public override async Task<string> GetResponse(string prompt, CancellationToken ct = default)
    {
        try
        {
            var workspacePath = Environment.GetEnvironmentVariable("IAW__Workspace")
                ?? Path.Combine(Path.GetTempPath(), "iaw-workspace");

            var slug = GenerateSlug(prompt);
            var taskId = $"{DateTime.UtcNow:yyyy-MM-dd}-{slug}-{Guid.NewGuid().ToString("N")[..6]}";
            var taskDir = Path.Combine(workspacePath, "tasks", taskId);
            Directory.CreateDirectory(taskDir);
            Directory.CreateDirectory(Path.Combine(taskDir, "output"));

            await File.WriteAllTextAsync(Path.Combine(taskDir, "plan.md"), prompt, ct);

            var code = await GenerateCode(prompt, ct);
            await File.WriteAllTextAsync(Path.Combine(taskDir, "orchestration.cs"), code, ct);
            await File.WriteAllTextAsync(Path.Combine(taskDir, "orchestration.csproj"),
                ScriptGenerator.GenerateCsproj(), ct);

            var (exitCode, log) = await ExecuteProject(taskDir, ct);
            await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), log, ct);

            if (exitCode != 0)
            {
                var errorTail = log.Length > 2000 ? log[^2000..] : log;
                return $"Execution failed (exit code {exitCode}).\nWorkspace: {taskDir}\n{errorTail}";
            }

            var resultPath = Path.Combine(taskDir, "result.json");
            if (File.Exists(resultPath))
                return await File.ReadAllTextAsync(resultPath, ct);

            var outputTail = log.Length > 1000 ? log[^1000..] : log;
            return $"Completed. Workspace: {taskDir}\n{outputTail}";
        }
        catch (Exception ex)
        {
            return $"CodeOrchestrator error: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task<string> GenerateCode(string plan, CancellationToken ct)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, Instructions),
            new(Microsoft.Extensions.AI.ChatRole.User, plan)
        };
        var response = await ChatClient.GetResponseAsync(messages, cancellationToken: ct);
        var code = (response.Text ?? "").Trim();
        if (code.StartsWith("```"))
            code = code[(code.IndexOf('\n') + 1)..];
        if (code.EndsWith("```"))
            code = code[..^3].TrimEnd();
        return code;
    }

    private async Task<(int ExitCode, string Log)> ExecuteProject(string taskDir, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ExecutionTimeout);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{taskDir}\"",
            WorkingDirectory = taskDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var log = new StringBuilder();
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) log.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) log.AppendLine($"[stderr] {e.Data}"); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, log.ToString());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, log + "\n[Killed: execution timed out]");
        }
    }

    static string GenerateSlug(string plan)
    {
        var words = plan.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(w => w.Length > 0);
        var slug = string.Join("-", words);
        return slug.Length > 30 ? slug[..30] : slug;
    }
}
```

- [ ] **Step 2: Simplify ScriptGenerator to just .csproj template**

Replace `src/Core/Orchestration/ScriptGenerator.cs` with:

```csharp
namespace Core.Orchestration;

public static class ScriptGenerator
{
    public static string GenerateCsproj() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net11.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Aspire.IAW.Client" Version="*" />
          </ItemGroup>
        </Project>
        """;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Core && dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 4: Update CodeOrchestrator tests**

Update `test/Core.Tests/CodeOrchestratorTests.cs` — keep `ExecuteCodeOrchestration_CreatesWorkspaceFiles` and `ExecuteCodeOrchestration_ReturnsErrorOnBadPath` but change them to call `GetResponse` instead of `ExecuteCodeOrchestration`:

Replace `orchestrator.ExecuteCodeOrchestration(...)` with `orchestrator.GetResponse(...)` in both tests.

- [ ] **Step 5: Run tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor: simplify CodeOrchestrator to override GetResponse, clean ScriptGenerator"
```

---

## Chunk 3: New Execute Tool on Project Grain

### Task 3: Replace DelegateToAssistant/ExecuteWithCode with Execute tool

**Files:**
- Modify: `src/Agents/Projects/Project.cs`

- [ ] **Step 1: Read the file**

Read `src/Agents/Projects/Project.cs` in full.

- [ ] **Step 2: Replace Instructions with concise, tool-focused versions**

Replace the entire `Instructions` property. All variants follow the same pattern — concise, tool-focused, with markdown guidance:

```csharp
protected override string Instructions => GetTopicSlug() switch
{
    "general" => """
        You are the user's assistant. Be concise and direct. Use markdown formatting.

        TOOLS:
        - Execute: run any task via generated code (build, create files, search, deploy, etc.)
        - RequestApprovalTool: ask user to choose or confirm before acting
        - Recall: search past results and documents
        - ScheduleJobTool/CancelJobTool: manage recurring tasks
        - AddTaskTool/UpdateTaskTool/ListTasksTool: manage project tasks

        BEHAVIOR:
        - If you can answer from knowledge or memory, answer directly
        - If the request is clear, call Execute immediately
        - If ambiguous or risky, explain your plan, use RequestApprovalTool to confirm, then Execute
        - Never generate code in your response. Always use Execute.
        """,
    "personal" => """
        You are the user's personal assistant. Warm and helpful. Use markdown.
        Remember preferences and personal facts via memory.
        For any task requiring action, use Execute.
        For scheduling, use ScheduleJobTool.
        """,
    "iaw" => """
        You are the IAW project assistant. Use markdown.
        You can check Aspire resource health, read logs and traces.
        For any action (build, test, deploy, troubleshoot), use Execute.
        If ambiguous, confirm the plan first with RequestApprovalTool.
        """,
    "scheduled" => """
        You manage scheduled jobs and recurring tasks. Use markdown.
        Use ScheduleJobTool and CancelJobTool to manage jobs.
        Use Execute for one-off tasks.
        """,
    _ => """
        You are a project assistant. Be concise. Use markdown.

        TOOLS:
        - Execute: run any task via generated code
        - RequestApprovalTool: ask user to choose or confirm
        - Recall: search past results and documents
        - ScheduleJobTool/CancelJobTool: recurring tasks

        If the request is clear, call Execute immediately.
        If ambiguous, explain your plan and use RequestApprovalTool first.
        Never generate code in your response.
        """
};
```

- [ ] **Step 3: Replace DefineTools — remove DelegateToAssistant/ExecuteWithCode, add Execute**

```csharp
protected override IReadOnlyList<AITool> DefineTools()
{
    return
    [
        AIFunctionFactory.Create(Execute, nameof(Execute),
            "Execute a task by generating and running C# code that calls agent interfaces directly"),
        AIFunctionFactory.Create(RequestApprovalTool, nameof(RequestApprovalTool),
            "Ask the user to approve or decline something, or choose between options"),
        AIFunctionFactory.Create(RecallTool, nameof(RecallTool),
            "Search past task results, conversations, and documents"),
        AIFunctionFactory.Create(AddTaskTool, nameof(AddTaskTool),
            "Add a new task to the project board"),
        AIFunctionFactory.Create(UpdateTaskTool, nameof(UpdateTaskTool),
            "Update the status of an existing task"),
        AIFunctionFactory.Create(ListTasksTool, nameof(ListTasksTool),
            "List all tasks in the project"),
        AIFunctionFactory.Create(ScheduleJobTool, nameof(ScheduleJobTool),
            "Schedule a recurring job that runs on a timer"),
        AIFunctionFactory.Create(CancelJobTool, nameof(CancelJobTool),
            "Cancel an active scheduled job"),
        AIFunctionFactory.Create(ListJobsTool, nameof(ListJobsTool),
            "List all scheduled jobs"),
    ];
}
```

- [ ] **Step 4: Implement Execute tool method**

Add the Execute method. Delete `DelegateToAssistant` and `ExecuteWithCode` methods:

```csharp
[Description("Execute a task by generating and running C# code. " +
    "The code connects to the agent cluster and calls agents directly.")]
private async Task<string> Execute(
    [Description("What to do, step by step")] string plan)
{
    var orchestrator = GrainFactory.GetGrain<IAgent>("code-orchestrator");
    var result = await orchestrator.GetResponse(plan, CancellationToken.None);
    return result;
}
```

Note: uses `IAgent` (not `ICodeOrchestrator`) to avoid Orleans routing issues. The grain key `"code-orchestrator"` activates `CodeOrchestratorAgent` which overrides `GetResponse`.

- [ ] **Step 5: Clean up unused imports**

Remove `using IAW.Agents.Orchestration;` if no longer needed. Keep `using Core.Contracts;` etc.

- [ ] **Step 6: Build**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 7: Run tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 8: Commit**

```bash
git add src/Agents/Projects/Project.cs
git commit -m "refactor: replace DelegateToAssistant/ExecuteWithCode with single Execute tool"
```

---

## Chunk 4: MCP + Telegram Updates

### Task 4: Update MCP AgentTools

**Files:**
- Modify: `src/IAW.MCP/Tools/AgentTools.cs`

- [ ] **Step 1: Update AssistantChat to use project agent**

Replace the `AssistantChat` method to route to the project agent instead of deleted personal-assistant:

```csharp
[McpServerTool(Name = "assistant_chat")]
[Description("Send a message to the project assistant and get a response.")]
public async Task<string> AssistantChat(
    [Description("The message to send")] string message,
    [Description("Project ID (default: general)")] string projectId = "general",
    CancellationToken ct = default)
{
    var agent = ResolveAgent(projectId);
    var response = await agent.GetResponse(message, ct);
    return JsonSerializer.Serialize(new { agentId = projectId, response }, JsonOptions);
}
```

- [ ] **Step 2: Build MCP project**

Run: `dotnet build src/IAW.MCP -o /tmp/mcp-build`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/IAW.MCP/Tools/AgentTools.cs
git commit -m "refactor: MCP assistant_chat routes to project agent instead of deleted PA"
```

### Task 5: Update Telegram bot for MarkdownV2 formatting

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

- [ ] **Step 1: Read the file**

Read `src/Clients.Telegram/TelegramBotService.cs`.

- [ ] **Step 2: Update StreamResponseAsync to use MarkdownV2 for final edit**

In the `StreamResponseAsync` method, the final `EditSafe` call should attempt MarkdownV2 formatting. Update `EditSafe` to try MarkdownV2 first, fall back to plain text:

```csharp
private async Task EditSafe(long chatId, int messageId, string text)
{
    if (string.IsNullOrWhiteSpace(text)) return;
    try
    {
        await botClient.EditMessageTextAsync(chatId, messageId, text,
            parseMode: FormatStyles.MarkdownV2);
    }
    catch (BotRequestException)
    {
        // MarkdownV2 parse failed — send as plain text
        try
        {
            await botClient.EditMessageTextAsync(chatId, messageId, text);
        }
        catch (BotRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("message text is empty", StringComparison.OrdinalIgnoreCase))
        {
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Clients.Telegram -o /tmp/tg-build`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: Telegram EditSafe tries MarkdownV2, falls back to plain text"
```

---

## Chunk 5: Build, Test, Verify

### Task 6: Full build and test

- [ ] **Step 1: Build all key projects**

Run: `dotnet build src/Core && dotnet build src/Agents && dotnet build src/IAW.MCP -o /tmp/mcp-v && dotnet build src/Clients.Telegram -o /tmp/tg-v`
Expected: All build succeeded

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 3: Restart Aspire and verify**

Restart assistant and telegram resources via Aspire MCP. Verify:
- Resources start without errors
- No crash logs in assistant console

- [ ] **Step 4: Test via MCP**

Use MCP `assistant_chat` to send: "What is 2+2?" — should get direct answer (no execution).
Use MCP `assistant_chat` to send: "Create a hello world C# project at D:/Test" — should trigger Execute tool.

Check `D:/IAW-Workspace/tasks/` for generated files.

- [ ] **Step 5: Check Aspire traces**

Verify:
- Project grain activates, LLM calls Execute tool
- CodeOrchestrator grain activates, GetResponse override runs
- Workspace files created (plan.md, orchestration.cs, etc.)

- [ ] **Step 6: Commit any fixups**

```bash
git add -A
git commit -m "fix: address issues found during verification"
```

---

## Parallelization Guide

**Wave 1** (Task 1): Delete dead code — must go first, everything depends on clean compile
**Wave 2** (Tasks 2, 3, 4 in parallel): CodeOrchestrator rewrite, Project Execute tool, MCP update — all modify different files
**Wave 3** (Task 5): Telegram MarkdownV2 — independent
**Wave 4** (Task 6): Full verification — depends on all
