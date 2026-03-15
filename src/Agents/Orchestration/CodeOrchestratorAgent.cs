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
        "Return JSON: {\"action\":\"retry|skip\",\"reason\":\"...\"}";

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
}
