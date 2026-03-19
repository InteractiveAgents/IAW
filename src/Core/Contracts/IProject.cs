namespace Core.Contracts;

public interface IProject : IAgent
{
    Task<ProjectDashboard> GetDashboard(CancellationToken ct);
    Task<ProjectTask> AddTask(string description, TaskPriority priority, CancellationToken ct);
    Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct);
    Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct);
    new Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct);
    new Task CancelJob(string jobId, CancellationToken ct);
    Task RegisterFile(FileReference fileRef, CancellationToken ct);
    Task RequestApproval(string question, string[] options, CancellationToken ct);
    Task<ProjectContext> GetProjectContext(CancellationToken ct);
}
