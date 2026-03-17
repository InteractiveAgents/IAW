using System.Diagnostics;
using System.Text;
using System.Text.Json;
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

    private const string TaskPrefix = "orchestration-";
    private const int MaxSelfHealAttempts = 3;

    protected override string DisplayName => "Code Orchestrator";

    protected override string Instructions => """
        You are a code orchestrator. You receive a task plan and generate a standalone C# console application
        that executes the plan by calling IAW agent interfaces via the Aspire.IAW.Client package.

        The generated code must:
        1. Be a complete, compilable Program.cs for a .NET console app
        2. Use top-level statements
        3. Use `builder.AddIAWClient()` from Aspire.IAW.Client to connect to the Orleans cluster
        4. Call agent grain interfaces (IAgent.GetResponse, IAgent.GetResponseStream) for AI tasks
        5. Write a result.json file at the end with: status, summary, artifacts array, metrics object
        6. Write any output files to an "output" subdirectory
        7. Wrap the main logic in try/catch and report errors to result.json
        8. Print progress to stdout (it will be captured and streamed to the user)

        Available agent interfaces (all implement IAgent with GetResponse/GetResponseStream):
        - IFileSystem (file-system): read, write, search, list files
        - IShell (shell): execute shell commands
        - IBuild (build): compile and test .NET projects
        - IGit (git): version control operations
        - IReviewer (reviewer): code quality review
        - INotificationAgent (notification): send alerts

        Output ONLY the C# code. No markdown, no explanation. Just the code.
        """;

    protected override IReadOnlyList<AITool> DefineTools() => [];

    public async Task<string> ExecuteCodeOrchestration(string prompt, CancellationToken ct = default)
    {
        var workspacePath = Environment.GetEnvironmentVariable("IAW__Workspace")
            ?? Path.Combine(Path.GetTempPath(), "iaw-workspace");

        var slug = GenerateSlug(prompt);
        var taskId = $"{DateTime.UtcNow:yyyy-MM-dd}-{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        var taskDir = Path.Combine(workspacePath, "tasks", taskId);
        Directory.CreateDirectory(taskDir);
        Directory.CreateDirectory(Path.Combine(taskDir, "output"));

        WriteToolProgress($"Task: {taskId}\n");

        await File.WriteAllTextAsync(Path.Combine(taskDir, "plan.md"), prompt, ct);

        WriteToolProgress("Generating code...\n");
        var code = await GenerateCode(prompt, ct);
        var codePath = Path.Combine(taskDir, "orchestration.cs");
        await File.WriteAllTextAsync(codePath, code, ct);
        WriteToolProgress($"Code written to {codePath}\n");

        var csprojContent = GenerateCsproj();
        await File.WriteAllTextAsync(Path.Combine(taskDir, "orchestration.csproj"), csprojContent, ct);

        WriteToolProgress("Compiling and executing...\n");
        var (exitCode, log) = await ExecuteProject(taskDir, ct);
        await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), log, ct);

        if (exitCode != 0)
        {
            WriteToolProgress($"\nExecution failed (exit code {exitCode})\n");
            var errorSummary = log.Length > 2000 ? log[^2000..] : log;
            return $"Code execution failed (exit code {exitCode}). Last output:\n{errorSummary}";
        }

        var resultPath = Path.Combine(taskDir, "result.json");
        if (File.Exists(resultPath))
        {
            var resultJson = await File.ReadAllTextAsync(resultPath, ct);
            WriteToolProgress($"\nCompleted. Result: {resultJson}\n");
            return resultJson;
        }

        WriteToolProgress("\nCompleted (no result.json written).\n");
        var lastOutput = log.Length > 1000 ? log[^1000..] : log;
        return $"Execution completed but no result.json was written. Output:\n{lastOutput}";
    }

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
        var taskId = string.IsNullOrEmpty(plan.TaskId) ? await CreateTask(plan.Summary, ct) : plan.TaskId;
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
                    PublishCompletedAsync(taskId, line.Length > "[COMPLETED] ".Length ? line["[COMPLETED] ".Length..] : plan.Summary, artifacts).GetAwaiter().GetResult();
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
                if (healResult == "skip") break;

                errors.Clear();
                result = await executor.ExecuteScriptAsync(
                    script, workspace,
                    onOutputLine: line => { if (line.StartsWith("[PROGRESS:")) PublishProgressAsync(taskId, line).GetAwaiter().GetResult(); },
                    onErrorLine: line => { if (line.StartsWith("[ERROR:")) errors.Add(ParseError(line)); },
                    ct: ct);

                if (result.Success) break;
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

    private async Task<string> GenerateCode(string plan, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in base.GetResponseStream(plan, ct))
            sb.Append(chunk);

        var code = sb.ToString().Trim();
        if (code.StartsWith("```"))
        {
            var firstNewline = code.IndexOf('\n');
            code = code[(firstNewline + 1)..];
        }
        if (code.EndsWith("```"))
            code = code[..^3].TrimEnd();
        return code;
    }

    private static string GenerateCsproj() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net11.0</TargetFramework>
            <RootNamespace>Orchestration</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Aspire.IAW.Client" Version="*" />
          </ItemGroup>
        </Project>
        """;

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
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            log.AppendLine(e.Data);
            WriteToolProgress(e.Data + "\n");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            log.AppendLine($"[stderr] {e.Data}");
        };

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

    private async Task<string> SelfHealAsync(
        OrchestrationPlan plan, (int StepIndex, string ErrorType, string ErrorMessage) error,
        int attempt, CancellationToken ct)
    {
        var failingStep = plan.Steps.FirstOrDefault(s => s.Order == error.StepIndex);
        var prompt = $"Orchestration step {error.StepIndex} failed (attempt {attempt + 1}/{MaxSelfHealAttempts}). " +
            $"Step: {failingStep?.AgentType}.{failingStep?.Action}. " +
            $"Error: {error.ErrorType} - {error.ErrorMessage}. " +
            $"Critical: {failingStep?.Critical}. " +
            "Reply with JSON: {\"action\":\"retry|skip\",\"reason\":\"...\"}";

        var response = await base.GetResponse(prompt, ct);
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

    static (int StepIndex, string ErrorType, string ErrorMessage) ParseError(string line)
    {
        var bracketEnd = line.IndexOf(']');
        if (bracketEnd < 0) return (0, "Unknown", line);
        var stepStr = line["[ERROR:".Length..bracketEnd];
        var payload = bracketEnd + 2 < line.Length ? line[(bracketEnd + 2)..] : "";
        var pipeIndex = payload.IndexOf('|');
        int.TryParse(stepStr, out var stepIndex);
        return pipeIndex >= 0
            ? (stepIndex, payload[..pipeIndex], payload[(pipeIndex + 1)..])
            : (stepIndex, "Unknown", payload);
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

    private static string GenerateSlug(string plan)
    {
        var words = plan.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(w => w.Length > 0);
        var slug = string.Join("-", words);
        return slug.Length > 30 ? slug[..30] : slug;
    }
}
