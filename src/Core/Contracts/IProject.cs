namespace Core.Contracts;

public interface IProject : IAgent
{
    Task<ProjectDashboard> GetDashboard(CancellationToken ct);
    Task<ProjectTask> AddTask(string description, TaskPriority priority, CancellationToken ct);
    Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct);
    Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct);
    Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct);
    Task CancelJob(string jobId, CancellationToken ct);
    Task RegisterFile(FileReference fileRef, CancellationToken ct);
    Task RequestApproval(string question, string[] options, CancellationToken ct);
    Task<ProjectContext> GetProjectContext(CancellationToken ct);
}
