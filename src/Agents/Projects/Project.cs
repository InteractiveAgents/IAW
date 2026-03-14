using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Projects;

[GrainType("project-v1")]
public class Project(
    [ProjectState] ProjectDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient), IProject
{
    protected override string Instructions => """
        You are a project assistant. Help the user manage their project,
        answer questions, and coordinate tasks.
        Be concise and actionable in your responses.
        """;
    protected override string DisplayName => "Project";

    public Task<ProjectDashboard> GetDashboard(CancellationToken ct) =>
        Task.FromResult(new ProjectDashboard());

    public Task<ProjectTask> AddTask(string description, TaskPriority priority, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectTask>>([]);

    public Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task CancelJob(string jobId, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task RegisterFile(FileReference fileRef, CancellationToken ct) =>
        Task.CompletedTask;

    public Task RequestApproval(string question, string[] options, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 2");

    public Task<ProjectContext> GetProjectContext(CancellationToken ct) =>
        Task.FromResult(new ProjectContext());
}
