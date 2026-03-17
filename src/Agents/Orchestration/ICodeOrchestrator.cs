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
    Task<string> ExecuteCodeOrchestration(string plan, CancellationToken ct = default);
}

[GenerateSerializer]
public record TaskState(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Description,
    [property: Id(2)] OrchestrationStatus Status,
    [property: Id(3)] IReadOnlyList<StepRecord> Steps,
    [property: Id(4)] DateTimeOffset CreatedAt,
    [property: Id(5)] DateTimeOffset? CompletedAt,
    [property: Id(6)] IReadOnlyList<string>? ArtifactPaths = null);
