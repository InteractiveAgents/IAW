using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Orchestration;

public class TaskSupervisorAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), ITaskSupervisor
{
    protected override string DisplayName => "Task Supervisor";
    protected override string Instructions =>
        "You are the Task Supervisor, the IAW team's progress monitor. " +
        "You track active tasks, detect stalls, and escalate blockers. " +
        "Report task health status concisely — completed steps, stall duration, and recommended actions.";

    private const string TaskPrefix = "task-health-";

    public async Task RegisterTask(string taskId, string orchestratorId, int stepCount, CancellationToken ct = default)
    {
        var record = new TaskHealthRecord(taskId, orchestratorId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, stepCount, 0, false, null);
        State[$"{TaskPrefix}{taskId}"] = new StateEntry($"{TaskPrefix}{taskId}", JsonSerializer.Serialize(record));
        await WriteStateAsync(ct);
    }

    public async Task ReportProgress(string taskId, int completedSteps, CancellationToken ct = default)
    {
        var key = $"{TaskPrefix}{taskId}";
        if (!State.TryGetValue(key, out var entry)) return;

        var record = JsonSerializer.Deserialize<TaskHealthRecord>(entry.Value.ToString()!);
        if (record is null) return;

        record = record with { CompletedSteps = completedSteps, LastProgressAt = DateTimeOffset.UtcNow, IsStalled = false, StallReason = null };
        State[key] = new StateEntry(key, JsonSerializer.Serialize(record));
        await WriteStateAsync(ct);
    }

    public Task<TaskHealthRecord?> GetTaskHealth(string taskId, CancellationToken ct = default)
    {
        var key = $"{TaskPrefix}{taskId}";
        if (!State.TryGetValue(key, out var entry)) return Task.FromResult<TaskHealthRecord?>(null);
        return Task.FromResult(JsonSerializer.Deserialize<TaskHealthRecord>(entry.Value.ToString()!));
    }

    public Task<IReadOnlyList<TaskHealthRecord>> GetAllActiveTaskHealth(CancellationToken ct = default)
    {
        var records = State
            .Where(kvp => kvp.Key.StartsWith(TaskPrefix))
            .Select(kvp => JsonSerializer.Deserialize<TaskHealthRecord>(kvp.Value.Value.ToString()!))
            .Where(r => r is not null)
            .Cast<TaskHealthRecord>()
            .ToList();
        return Task.FromResult<IReadOnlyList<TaskHealthRecord>>(records);
    }
}
