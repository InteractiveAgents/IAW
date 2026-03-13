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
        "You are the Code Orchestrator, the IAW team's multi-step task coordinator. " +
        "You decompose complex code tasks into ordered steps, assign them to agents, and track progress. " +
        "Report step completions and failures concisely.";

    private const string TaskPrefix = "orchestration-";

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
    {
        await UpdateTaskStatus(taskId, OrchestrationStatus.Paused, ct);
    }

    public async Task ResumeTask(string taskId, CancellationToken ct = default)
    {
        await UpdateTaskStatus(taskId, OrchestrationStatus.Running, ct);
    }

    private async Task UpdateTaskStatus(string taskId, OrchestrationStatus status, CancellationToken ct)
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
