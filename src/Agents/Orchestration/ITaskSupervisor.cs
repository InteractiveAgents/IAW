using Core.Contracts;
using Core.Models;

namespace IAW.Agents.Orchestration;

public interface ITaskSupervisor : IAgent
{
    Task RegisterTask(string taskId, string orchestratorId, int stepCount, CancellationToken ct = default);
    Task ReportProgress(string taskId, int completedSteps, CancellationToken ct = default);
    Task<TaskHealthRecord?> GetTaskHealth(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskHealthRecord>> GetAllActiveTaskHealth(CancellationToken ct = default);
}
