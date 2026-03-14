using System.ComponentModel;
using Core.AI;
using Core.AI.Models;
using Core.Context;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

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

    private IReadOnlyList<IAgentContextProvider>? _contextProviders;

    protected override IReadOnlyList<IAgentContextProvider> GetContextProviders()
    {
        if (_contextProviders is not null) return _contextProviders;

        var qdrant = ServiceProvider.GetService<QdrantClient>();
        var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        _contextProviders = qdrant is not null && embeddings is not null
            ? [new RAGContextProvider(qdrant, embeddings)]
            : [];
        return _contextProviders;
    }

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(RequestApprovalTool, nameof(RequestApprovalTool),
                "Ask the user to approve or decline something. Returns when approval is requested."),
            AIFunctionFactory.Create(AddTaskTool, nameof(AddTaskTool),
                "Add a new task to the project board."),
            AIFunctionFactory.Create(UpdateTaskTool, nameof(UpdateTaskTool),
                "Update the status of an existing task."),
            AIFunctionFactory.Create(ListTasksTool, nameof(ListTasksTool),
                "List all tasks in the project."),
        ];
    }

    [Description("Request user approval with a question and a set of options")]
    private async Task<string> RequestApprovalTool(
        [Description("The question to ask the user")] string question,
        [Description("Available options for the user to choose from")] string[] options)
    {
        var approvalId = Guid.NewGuid().ToString("N")[..8];
        await PublishAsync("approval.requested", new Dictionary<string, object>
        {
            ["approvalId"] = approvalId,
            ["question"] = question,
            ["options"] = options,
            ["projectSlug"] = this.GetPrimaryKeyString()
        });
        return $"Approval requested (id: {approvalId}). Waiting for user response.";
    }

    [Description("Add a task to the project board")]
    private async Task<string> AddTaskTool(
        [Description("Task description")] string description,
        [Description("Priority: Low, Medium, High, or Critical")] TaskPriority priority)
    {
        var task = await AddTask(description, priority, CancellationToken.None);
        return $"Task '{task.Id}' created: {task.Description} ({task.Priority})";
    }

    [Description("Update task status")]
    private async Task<string> UpdateTaskTool(
        [Description("Task ID")] string taskId,
        [Description("New status: Pending, InProgress, Done, or Cancelled")] ProjectTaskStatus status)
    {
        await UpdateTask(taskId, status, CancellationToken.None);
        return $"Task '{taskId}' updated to {status}";
    }

    [Description("List all project tasks")]
    private Task<string> ListTasksTool()
    {
        var tasks = durableState.Tasks;
        if (tasks.Count == 0) return Task.FromResult("No tasks.");
        var lines = tasks.Select(t => $"[{t.Id}] {t.Status}: {t.Description} ({t.Priority})");
        return Task.FromResult(string.Join("\n", lines));
    }

    public Task<ProjectDashboard> GetDashboard(CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public async Task<ProjectTask> AddTask(string description, TaskPriority priority, CancellationToken ct)
    {
        var task = new ProjectTask
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Description = description,
            Priority = priority,
            Status = ProjectTaskStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        durableState.Tasks.Add(task);
        return task;
    }

    public Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct)
    {
        var index = -1;
        for (var i = 0; i < durableState.Tasks.Count; i++)
        {
            if (durableState.Tasks[i].Id == taskId) { index = i; break; }
        }
        if (index < 0) throw new KeyNotFoundException($"Task {taskId} not found");

        var existing = durableState.Tasks[index];
        durableState.Tasks[index] = existing with
        {
            Status = status,
            CompletedAt = status is ProjectTaskStatus.Done or ProjectTaskStatus.Cancelled
                ? DateTimeOffset.UtcNow : existing.CompletedAt
        };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectTask>>(durableState.Tasks.ToList());

    public Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task CancelJob(string jobId, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task RegisterFile(FileReference fileRef, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 3");

    public async Task RequestApproval(string question, string[] options, CancellationToken ct)
    {
        await RequestApprovalTool(question, options);
    }

    public Task<ProjectContext> GetProjectContext(CancellationToken ct) =>
        Task.FromResult(new ProjectContext());
}
