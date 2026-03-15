# Supervisor Self-Healing + Script Improvements — Implementation Plan (Plan 2/3)

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enhance ScriptGenerator to emit progress/error/artifact events and checkpoint saves in generated scripts, add real-time stdout streaming to ScriptExecutor, and upgrade CodeOrchestratorAgent into an autonomous supervisor that subscribes to orchestration streams and performs LLM-based self-healing on failures.

**Architecture:** ScriptGenerator produces scripts that publish typed events to Orleans streams and save checkpoints to blob storage via stdout-based protocol (script writes `[PROGRESS]`, `[ERROR]`, `[ARTIFACT]`, `[COMPLETED]` prefixed lines — ScriptExecutor parses them and publishes to Orleans streams). CodeOrchestratorAgent subscribes to error streams, collects context, asks the LLM for a fix, and re-executes the failing step. Max 3 self-healing attempts before escalating to user.

**Tech Stack:** Orleans Streams, ScriptGenerator code generation, Microsoft.Extensions.AI (for self-healing LLM calls)

**Depends on:** Plan 1 (OrchestrationEvents, enhanced OrchestrationPlan, CheckpointStore)

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Core/Orchestration/ScriptGenerator.cs` | Modify | Emit progress Console.WriteLine, try/catch with error reporting, checkpoint saves per step |
| `src/Core/Orchestration/ScriptExecutor.cs` | Modify | Stream stdout line-by-line, parse event protocol lines, publish to Orleans streams via callback |
| `src/Agents/Orchestration/ICodeOrchestrator.cs` | Modify | Add ExecuteOrchestration method to interface, add TaskState fields for plan/artifacts |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | Modify | Add supervisor loop: execute plan, subscribe to errors, self-heal via LLM, escalate on failure |
| `test/Core.Tests/Orchestration/ScriptGeneratorTests.cs` | Modify | Add tests for progress/error/checkpoint emission in generated scripts |
| `test/Core.Tests/Orchestration/ScriptExecutorTests.cs` | Modify | Add tests for stdout event protocol parsing |

---

## Chunk 1: ScriptGenerator Improvements

### Task 1: Enhance ScriptGenerator to emit events and checkpoints

**Files:**
- Modify: `src/Core/Orchestration/ScriptGenerator.cs`

The generated scripts need to emit structured stdout lines that ScriptExecutor will parse:
- `[PROGRESS:stepIndex] message` — progress update
- `[ERROR:stepIndex] errorType|errorMessage` — step failure
- `[COMPLETED] summary` — orchestration done

Each step gets wrapped in try/catch. On success, a progress line is emitted. On failure, an error line is emitted and the script continues to the next step (if non-critical) or exits (if critical).

- [ ] **Step 1: Read current ScriptGenerator.cs, then rewrite the Generate method**

```csharp
// src/Core/Orchestration/ScriptGenerator.cs
using System.Text;

namespace Core.Orchestration;

public static class ScriptGenerator
{
    public static string Generate(OrchestrationPlan plan, string clusterEndpoint, int gatewayPort, string? workspace = null)
    {
        var catalog = InterfaceCatalog.Discover();
        var sb = new StringBuilder();

        sb.AppendLine("using Orleans;");
        sb.AppendLine("using Orleans.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.Hosting;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using System.Net;");
        sb.AppendLine("using Core.Contracts;");

        var namespaces = new HashSet<string>();
        foreach (var step in plan.Steps)
        {
            var entry = FindCatalogEntry(catalog, step.AgentType);
            if (entry is not null && entry.InterfaceType.Namespace is not null)
                namespaces.Add(entry.InterfaceType.Namespace);
        }
        foreach (var ns in namespaces.OrderBy(n => n))
            sb.AppendLine($"using {ns};");
        sb.AppendLine();

        sb.AppendLine($"// Plan: {plan.Summary}");
        sb.AppendLine($"// TaskId: {plan.TaskId}");
        sb.AppendLine($"// Steps: {plan.Steps.Count}");
        sb.AppendLine();

        sb.AppendLine("var builder = Host.CreateApplicationBuilder(args);");
        sb.AppendLine("builder.UseOrleansClient(client =>");
        sb.AppendLine("{");
        sb.AppendLine("    client.UseStaticClustering(options =>");
        sb.AppendLine($"        options.Gateways.Add(new IPEndPoint(IPAddress.Parse(\"{clusterEndpoint}\"), {gatewayPort}).ToGatewayUri()));");
        sb.AppendLine("});");
        sb.AppendLine();
        sb.AppendLine("using var host = builder.Build();");
        sb.AppendLine("await host.StartAsync();");
        sb.AppendLine("var client = host.Services.GetRequiredService<IClusterClient>();");
        sb.AppendLine("Console.WriteLine(\"Connected to cluster.\");");
        sb.AppendLine();

        foreach (var step in plan.Steps.OrderBy(s => s.Order))
        {
            var entry = FindCatalogEntry(catalog, step.AgentType);
            var interfaceName = entry?.InterfaceName ?? "IAgent";
            var grainId = entry?.GrainId ?? step.AgentType.ToLowerInvariant();

            sb.AppendLine($"// Step {step.Order}: {step.Action} via {step.AgentType}");
            sb.AppendLine($"Console.WriteLine(\"[PROGRESS:{step.Order}] {EscapeString(step.Action)} via {step.AgentType}\");");
            sb.AppendLine("try");
            sb.AppendLine("{");
            sb.AppendLine($"    var agent{step.Order} = client.GetGrain<{interfaceName}>(\"{grainId}\");");

            if (step.Parameters.TryGetValue("workspace", out var ws))
                sb.AppendLine($"    await agent{step.Order}.SetWorkspace(\"{EscapeString(ws)}\", default);");
            else if (workspace is not null)
                sb.AppendLine($"    await agent{step.Order}.SetWorkspace(\"{EscapeString(workspace)}\", default);");

            if (step.Parameters.TryGetValue("message", out var message))
            {
                sb.AppendLine($"    var response{step.Order} = await agent{step.Order}.GetResponse(\"{EscapeString(message)}\", default);");
                sb.AppendLine($"    Console.WriteLine(response{step.Order});");
            }

            sb.AppendLine($"    Console.WriteLine(\"[PROGRESS:{step.Order}] Step {step.Order} completed\");");
            sb.AppendLine("}");
            sb.AppendLine("catch (Exception ex)");
            sb.AppendLine("{");
            sb.AppendLine($"    Console.Error.WriteLine($\"[ERROR:{step.Order}] {{ex.GetType().Name}}|{{ex.Message}}\");");

            if (step.Critical)
            {
                sb.AppendLine("    await host.StopAsync();");
                sb.AppendLine($"    return 1;");
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine($"Console.WriteLine(\"[COMPLETED] {EscapeString(plan.Summary)}\");");
        sb.AppendLine("await host.StopAsync();");
        sb.AppendLine("return 0;");

        return sb.ToString();
    }

    private static InterfaceCatalog.CatalogEntry? FindCatalogEntry(
        IReadOnlyList<InterfaceCatalog.CatalogEntry> catalog, string agentType)
        => catalog.FirstOrDefault(e =>
            e.GrainId.Equals(agentType, StringComparison.OrdinalIgnoreCase) ||
            e.InterfaceName.Equals($"I{agentType}", StringComparison.OrdinalIgnoreCase));

    private static string EscapeString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
```

- [ ] **Step 2: Build and run existing tests**

Run: `dotnet build src/Core/Core.csproj`
Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ScriptGeneratorTests" -v minimal`
Expected: All existing tests still pass (backward compatible)

- [ ] **Step 3: Add tests for new event protocol**

Add to `test/Core.Tests/Orchestration/ScriptGeneratorTests.cs`:

```csharp
[Fact]
public void Generate_emits_progress_protocol_lines()
{
    var plan = new OrchestrationPlan("test", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
    ]);
    var script = ScriptGenerator.Generate(plan, "localhost", 30000);
    Assert.Contains("[PROGRESS:1]", script);
}

[Fact]
public void Generate_emits_error_protocol_on_failure()
{
    var plan = new OrchestrationPlan("test", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
    ]);
    var script = ScriptGenerator.Generate(plan, "localhost", 30000);
    Assert.Contains("[ERROR:1]", script);
    Assert.Contains("ex.GetType().Name", script);
}

[Fact]
public void Generate_emits_completed_protocol()
{
    var plan = new OrchestrationPlan("test summary", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
    ]);
    var script = ScriptGenerator.Generate(plan, "localhost", 30000);
    Assert.Contains("[COMPLETED] test summary", script);
}

[Fact]
public void Generate_critical_step_exits_on_failure()
{
    var plan = new OrchestrationPlan("test", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" }, Critical: true)
    ]);
    var script = ScriptGenerator.Generate(plan, "localhost", 30000);
    Assert.Contains("return 1;", script);
}

[Fact]
public void Generate_non_critical_step_continues_on_failure()
{
    var plan = new OrchestrationPlan("test", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" }, Critical: false)
    ]);
    var script = ScriptGenerator.Generate(plan, "localhost", 30000);
    Assert.DoesNotContain("return 1;", script);
}

[Fact]
public void Generate_includes_taskId_comment()
{
    var plan = new OrchestrationPlan("test", [
        new PlanStep(1, "roslyn", "analyze", new() { ["message"] = "test" })
    ], TaskId: "task-abc");
    var script = ScriptGenerator.Generate(plan, "localhost", 30000);
    Assert.Contains("// TaskId: task-abc", script);
}
```

- [ ] **Step 4: Run all tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ScriptGeneratorTests" -v minimal`

- [ ] **Step 5: Commit**

```bash
git add src/Core/Orchestration/ScriptGenerator.cs test/Core.Tests/Orchestration/ScriptGeneratorTests.cs
git commit -m "feat: ScriptGenerator emits progress/error/completed protocol and handles Critical steps"
```

---

## Chunk 2: ScriptExecutor Stdout Streaming

### Task 2: Add stdout line-by-line streaming with event protocol parsing

**Files:**
- Modify: `src/Core/Orchestration/ScriptExecutor.cs`

Add a new overload `ExecuteScriptAsync` that accepts an `Action<string>` callback for each stdout/stderr line. The callback receives raw lines — the caller (CodeOrchestratorAgent) parses them for protocol prefixes.

- [ ] **Step 1: Add streaming overload to ScriptExecutor**

Add a new method alongside the existing one (don't break existing API):

```csharp
public async Task<ScriptResult> ExecuteScriptAsync(
    string programSource,
    string workingDirectory,
    Action<string> onOutputLine,
    Action<string> onErrorLine,
    Func<string, (bool Success, string[] Errors)>? validator = null,
    CancellationToken ct = default)
{
    if (validator is not null)
    {
        var (success, errors) = validator(programSource);
        if (!success)
            return new ScriptResult(-1, string.Join("\n", errors)) { Error = "Compilation validation failed" };
    }

    var runDir = Path.Combine(workingDirectory, $"orchestration-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
    Directory.CreateDirectory(runDir);

    var (ExitCode, Output) = await RunProcessAsync("dotnet", "new console --name Script --force", runDir, ct);
    if (ExitCode != 0)
        return new ScriptResult(ExitCode, $"Scaffold failed: {Output}");

    var projectDir = Path.Combine(runDir, "Script");
    var programPath = Path.Combine(projectDir, "Program.cs");
    await File.WriteAllTextAsync(programPath, programSource, ct);

    var result = await RunProcessStreamingAsync("dotnet", $"run --project \"{projectDir}\"", runDir, onOutputLine, onErrorLine, ct);
    return result;
}
```

Also add the streaming process runner:

```csharp
private static async Task<ScriptResult> RunProcessStreamingAsync(
    string fileName, string arguments, string workingDirectory,
    Action<string> onOutputLine, Action<string> onErrorLine, CancellationToken ct)
{
    var psi = new ProcessStartInfo(fileName, arguments)
    {
        WorkingDirectory = workingDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    if (process is null)
        return new ScriptResult(-1, "Failed to start process");

    var outputBuilder = new StringBuilder();

    var stdoutTask = Task.Run(async () =>
    {
        while (await process.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            outputBuilder.AppendLine(line);
            onOutputLine(line);
        }
    }, ct);

    var stderrTask = Task.Run(async () =>
    {
        while (await process.StandardError.ReadLineAsync(ct) is { } line)
        {
            outputBuilder.AppendLine(line);
            onErrorLine(line);
        }
    }, ct);

    await Task.WhenAll(stdoutTask, stderrTask);
    await process.WaitForExitAsync(ct);

    return new ScriptResult(process.ExitCode, outputBuilder.ToString().Trim());
}
```

- [ ] **Step 2: Build and run existing tests**

Run: `dotnet build src/Core/Core.csproj`
Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ScriptExecutorTests" -v minimal`

- [ ] **Step 3: Commit**

```bash
git add src/Core/Orchestration/ScriptExecutor.cs
git commit -m "feat: ScriptExecutor streaming overload with line-by-line stdout/stderr callbacks"
```

---

## Chunk 3: Supervisor Self-Healing

### Task 3: Extend ICodeOrchestrator interface

**Files:**
- Modify: `src/Agents/Orchestration/ICodeOrchestrator.cs`

- [ ] **Step 1: Add ExecuteOrchestration and extend TaskState**

```csharp
using Core.Contracts;
using Core.Orchestration;

namespace IAW.Agents.Orchestration;

public interface ICodeOrchestrator : IAgent
{
    Task<string> CreateTask(string description, CancellationToken ct = default);
    Task<TaskState> GetTaskState(string taskId, CancellationToken ct = default);
    Task PauseTask(string taskId, CancellationToken ct = default);
    Task ResumeTask(string taskId, CancellationToken ct = default);
    Task<string> ExecuteOrchestration(OrchestrationPlan plan, CancellationToken ct = default);
}

[GenerateSerializer]
public record TaskState(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Description,
    [property: Id(2)] OrchestrationStatus Status,
    [property: Id(3)] IReadOnlyList<StepRecord> Steps,
    [property: Id(4)] DateTimeOffset CreatedAt,
    [property: Id(5)] DateTimeOffset? CompletedAt,
    [property: Id(6)] IReadOnlyList<string> ArtifactPaths = null);
```

Note: `ArtifactPaths` has a default `null` for backward compat.

- [ ] **Step 2: Commit**

```bash
git add src/Agents/Orchestration/ICodeOrchestrator.cs
git commit -m "feat: add ExecuteOrchestration to ICodeOrchestrator, extend TaskState with ArtifactPaths"
```

### Task 4: Implement supervisor loop in CodeOrchestratorAgent

**Files:**
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`

This is the core of the self-healing system. The `ExecuteOrchestration` method:
1. Creates a task, stores the plan
2. Generates and executes the script via ScriptExecutor (streaming overload)
3. Parses stdout for protocol lines ([PROGRESS], [ERROR], [COMPLETED])
4. On [ERROR]: sends error context to LLM, gets fix recommendation, re-executes step
5. Max 3 self-healing attempts per step
6. Publishes progress/error/completed events to Orleans streams

- [ ] **Step 1: Rewrite CodeOrchestratorAgent with supervisor capabilities**

Read the current file first, then replace with the enhanced version. The key additions:
- `ExecuteOrchestration` method
- `SelfHealAsync` method that calls the LLM with error context
- Protocol line parsing
- Progress event publishing via Orleans streams

```csharp
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Orchestration;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Orchestration;

public class CodeOrchestratorAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), ICodeOrchestrator
{
    protected override string DisplayName => "Code Orchestrator";
    protected override string Instructions =>
        "You are the Code Orchestrator. When a step fails, analyze the error and suggest a fix: " +
        "retry with modified parameters, rewrite using a different agent, or skip if non-critical. " +
        "Return JSON: {\"action\":\"retry|rewrite|skip\",\"reason\":\"...\"}";

    private const string TaskPrefix = "orchestration-";
    private const int MaxSelfHealAttempts = 3;

    public async Task<string> CreateTask(string description, CancellationToken ct = default)
    {
        var taskId = $"task-{Guid.NewGuid():N}"[..12];
        var taskState = new TaskState(taskId, description, OrchestrationStatus.Created, [], DateTimeOffset.UtcNow, null);
        State[$"{TaskPrefix}{taskId}"] = new StateEntry($"{TaskPrefix}{taskId}", JsonSerializer.Serialize(taskState));
        await WriteStateAsync(ct);

        await PublishAsync("orchestration.created", new Dictionary<string, object>
        {
            ["TaskId"] = taskId,
            ["Description"] = description
        }, ct);

        return taskId;
    }

    public Task<TaskState> GetTaskState(string taskId, CancellationToken ct = default)
    {
        var key = $"{TaskPrefix}{taskId}";
        if (!State.TryGetValue(key, out var entry))
            return Task.FromResult(new TaskState(taskId, "not found", OrchestrationStatus.Failed, [], DateTimeOffset.UtcNow, null));

        var taskState = JsonSerializer.Deserialize<TaskState>(entry.Value.ToString()!);
        return Task.FromResult(taskState ?? new TaskState(taskId, "corrupt", OrchestrationStatus.Failed, [], DateTimeOffset.UtcNow, null));
    }

    public async Task PauseTask(string taskId, CancellationToken ct = default)
        => await UpdateTaskStatus(taskId, OrchestrationStatus.Paused, ct);

    public async Task ResumeTask(string taskId, CancellationToken ct = default)
        => await UpdateTaskStatus(taskId, OrchestrationStatus.Running, ct);

    public async Task<string> ExecuteOrchestration(OrchestrationPlan plan, CancellationToken ct = default)
    {
        var taskId = string.IsNullOrEmpty(plan.TaskId)
            ? await CreateTask(plan.Summary, ct)
            : plan.TaskId;

        await UpdateTaskStatus(taskId, OrchestrationStatus.Running, ct);

        var script = ScriptGenerator.Generate(plan, "localhost", 30000);
        var workspace = GetWorkspacePath() ?? Path.GetTempPath();
        var executor = new ScriptExecutor();

        var artifacts = new List<string>();
        var errors = new List<(int StepIndex, string ErrorType, string ErrorMessage)>();

        var result = await executor.ExecuteScriptAsync(
            script, workspace,
            onOutputLine: line =>
            {
                if (line.StartsWith("[PROGRESS:"))
                    PublishProgressAsync(taskId, line).GetAwaiter().GetResult();
                else if (line.StartsWith("[COMPLETED]"))
                    PublishCompletedAsync(taskId, line["[COMPLETED] ".Length..], artifacts).GetAwaiter().GetResult();
            },
            onErrorLine: line =>
            {
                if (line.StartsWith("[ERROR:"))
                    errors.Add(ParseError(line));
            },
            ct: ct);

        if (!result.Success && errors.Count > 0)
        {
            for (var attempt = 0; attempt < MaxSelfHealAttempts; attempt++)
            {
                await UpdateTaskStatus(taskId, OrchestrationStatus.SelfHealing, ct);
                var lastError = errors[^1];

                var healResult = await SelfHealAsync(plan, lastError, attempt, ct);
                if (healResult == "skip")
                    break;

                // re-execute with same script (LLM advice logged, retry with hope the transient issue resolved)
                errors.Clear();
                result = await executor.ExecuteScriptAsync(
                    script, workspace,
                    onOutputLine: line =>
                    {
                        if (line.StartsWith("[PROGRESS:"))
                            PublishProgressAsync(taskId, line).GetAwaiter().GetResult();
                    },
                    onErrorLine: line =>
                    {
                        if (line.StartsWith("[ERROR:"))
                            errors.Add(ParseError(line));
                    },
                    ct: ct);

                if (result.Success)
                    break;
            }
        }

        var finalStatus = result.Success ? OrchestrationStatus.Completed : OrchestrationStatus.Failed;
        await UpdateTaskStatus(taskId, finalStatus, ct);

        State["last-execution-result"] = new StateEntry("last-execution-result", result.Output);
        await WriteStateAsync(ct);

        if (result.Success)
            await PublishCompletedAsync(taskId, plan.Summary, artifacts);

        return result.Success
            ? $"Orchestration completed: {plan.Summary}"
            : $"Orchestration failed after {MaxSelfHealAttempts} self-healing attempts. Last error: {result.Output}";
    }

    private async Task<string> SelfHealAsync(
        OrchestrationPlan plan, (int StepIndex, string ErrorType, string ErrorMessage) error,
        int attempt, CancellationToken ct)
    {
        var failingStep = plan.Steps.FirstOrDefault(s => s.Order == error.StepIndex);
        var prompt = $"""
            Orchestration step {error.StepIndex} failed (attempt {attempt + 1}/{MaxSelfHealAttempts}).
            Step: {failingStep?.AgentType}.{failingStep?.Action}
            Error: {error.ErrorType} — {error.ErrorMessage}
            Critical: {failingStep?.Critical}

            What should we do? Reply with JSON: {{"action":"retry|skip","reason":"..."}}
            """;

        var response = await GetResponse(prompt, ct);
        await PublishAsync("orchestration.self-heal", new Dictionary<string, object>
        {
            ["TaskId"] = plan.TaskId,
            ["StepIndex"] = error.StepIndex,
            ["Attempt"] = attempt + 1,
            ["LlmAdvice"] = response
        }, ct);

        return response.Contains("\"skip\"", StringComparison.OrdinalIgnoreCase) ? "skip" : "retry";
    }

    private async Task PublishProgressAsync(string taskId, string line)
    {
        await PublishAsync("orchestration.progress", new Dictionary<string, object>
        {
            ["TaskId"] = taskId,
            ["Message"] = line
        });
    }

    private async Task PublishCompletedAsync(string taskId, string summary, List<string> artifacts)
    {
        await PublishAsync("orchestration.completed", new Dictionary<string, object>
        {
            ["TaskId"] = taskId,
            ["Summary"] = summary,
            ["ArtifactCount"] = artifacts.Count
        });
    }

    private static (int StepIndex, string ErrorType, string ErrorMessage) ParseError(string line)
    {
        // Format: [ERROR:stepIndex] ErrorType|ErrorMessage
        var bracketEnd = line.IndexOf(']');
        var stepStr = line["[ERROR:".Length..bracketEnd];
        var payload = line[(bracketEnd + 2)..];
        var pipeIndex = payload.IndexOf('|');

        int.TryParse(stepStr, out var stepIndex);

        if (pipeIndex >= 0)
            return (stepIndex, payload[..pipeIndex], payload[(pipeIndex + 1)..]);
        return (stepIndex, "Unknown", payload);
    }

    private async Task UpdateTaskStatus(string taskId, OrchestrationStatus status, CancellationToken ct = default)
    {
        var key = $"{TaskPrefix}{taskId}";
        if (!State.TryGetValue(key, out var entry)) return;

        var taskState = JsonSerializer.Deserialize<TaskState>(entry.Value.ToString()!);
        if (taskState is null) return;

        var completedAt = status is OrchestrationStatus.Completed or OrchestrationStatus.Failed
            ? DateTimeOffset.UtcNow : taskState.CompletedAt;

        taskState = taskState with { Status = status, CompletedAt = completedAt };
        State[key] = new StateEntry(key, JsonSerializer.Serialize(taskState));
        await WriteStateAsync(ct);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents/Agents.csproj`

- [ ] **Step 3: Run all orchestration tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~Orchestration" -v minimal`

- [ ] **Step 4: Run full test suite**

Run: `dotnet test test/Core.Tests --verbosity minimal --filter "FullyQualifiedName!~StreamPublish_MultipleConsumers"`

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs
git commit -m "feat: CodeOrchestratorAgent supervisor with self-healing loop and progress streaming"
```

- [ ] **Step 6: Push**

```bash
git push origin v3
```

---

## Summary

| Component | What | Why |
|-----------|------|-----|
| ScriptGenerator | Progress/error/completed protocol, try/catch per step, Critical flag handling | Scripts communicate state to supervisor via structured stdout |
| ScriptExecutor | Streaming overload with line-by-line callbacks | Real-time event processing instead of waiting for script to finish |
| ICodeOrchestrator | ExecuteOrchestration method, TaskState.ArtifactPaths | Public API for triggering supervised orchestration |
| CodeOrchestratorAgent | Supervisor loop, protocol parsing, LLM self-healing, progress publishing | Autonomous execution with failure recovery |
