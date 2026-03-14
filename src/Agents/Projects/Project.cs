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

        var providers = new List<IAgentContextProvider>
        {
            new UserContextProvider(GrainFactory),
            new ProjectContextProvider(durableState.Tasks, durableState.Files),
            new TaskContextProvider(durableState.Tasks)
        };

        var qdrant = ServiceProvider.GetService<QdrantClient>();
        var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (qdrant is not null && embeddings is not null)
            providers.Add(new RAGContextProvider(qdrant, embeddings));

        var memoryAgents = ServiceProvider.GetService<IReadOnlyList<IMemoryAgent>>();
        if (memoryAgents is not null && memoryAgents.Count > 0)
            providers.Add(new MemoryContextProvider(memoryAgents));

        _contextProviders = providers;
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
            AIFunctionFactory.Create(ScheduleJobTool, nameof(ScheduleJobTool),
                "Schedule a recurring job that runs on a timer."),
            AIFunctionFactory.Create(CancelJobTool, nameof(CancelJobTool),
                "Cancel an active scheduled job."),
            AIFunctionFactory.Create(ListJobsTool, nameof(ListJobsTool),
                "List all scheduled jobs."),
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
        Task.FromResult(new ProjectDashboard
        {
            Tasks = durableState.Tasks.ToList(),
            Jobs = durableState.Schedules.Values.ToList(),
            Files = durableState.Files.Values.ToList(),
            GeneratedAt = DateTimeOffset.UtcNow
        });

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
        await PublishDashboardChanged();
        return task;
    }

    public async Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct)
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
        await PublishDashboardChanged();
    }

    public Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectTask>>(durableState.Tasks.ToList());

    public async Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct)
    {
        var job = new ScheduledJob
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Description = description,
            Interval = interval,
            NextRunAt = DateTimeOffset.UtcNow + interval,
            Active = true
        };
        durableState.Schedules[job.Id] = job;
        var trackingItem = new TrackingItem(job.Id, job.Description, interval, DateTimeOffset.UtcNow, null, null);
        await StartTrackingAsync(job.Id, trackingItem, interval, ct);
        await PublishDashboardChanged();
        return job;
    }

    public async Task CancelJob(string jobId, CancellationToken ct)
    {
        if (!durableState.Schedules.TryGetValue(jobId, out var job))
            throw new KeyNotFoundException($"Job {jobId} not found");

        durableState.Schedules[jobId] = job with { Active = false };
        await StopTrackingAsync(jobId, ct);
        await PublishDashboardChanged();
    }

    public async Task RegisterFile(FileReference fileRef, CancellationToken ct)
    {
        durableState.Files[fileRef.FileName] = fileRef;
        await PublishDashboardChanged();
    }

    public async Task RequestApproval(string question, string[] options, CancellationToken ct)
    {
        await RequestApprovalTool(question, options);
    }

    public Task<ProjectContext> GetProjectContext(CancellationToken ct) =>
        Task.FromResult(new ProjectContext());

    protected override async Task OnTrackingDueAsync(TrackingItem item, CancellationToken ct)
    {
        if (!durableState.Schedules.TryGetValue(item.Id, out var job) || !job.Active)
        {
            await base.OnTrackingDueAsync(item, ct);
            return;
        }

        var response = await GetResponse(item.Description, ct);
        durableState.Schedules[job.Id] = job with
        {
            LastRunAt = DateTimeOffset.UtcNow,
            NextRunAt = DateTimeOffset.UtcNow + job.Interval,
            LastResult = response
        };
        await PublishDashboardChanged();
    }

    [Description("Schedule a recurring job")]
    private async Task<string> ScheduleJobTool(
        [Description("Job name")] string name,
        [Description("Interval in minutes between runs")] int intervalMinutes,
        [Description("What the job should do each run")] string description)
    {
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var job = await ScheduleJob(name, interval, description, CancellationToken.None);
        return $"Job '{job.Id}' scheduled: {job.Name} — runs every {intervalMinutes} minutes";
    }

    [Description("Cancel a scheduled job")]
    private async Task<string> CancelJobTool(
        [Description("Job ID to cancel")] string jobId)
    {
        await CancelJob(jobId, CancellationToken.None);
        return $"Job '{jobId}' cancelled";
    }

    [Description("List all scheduled jobs")]
    private Task<string> ListJobsTool()
    {
        var jobs = durableState.Schedules.Values;
        if (!jobs.Any()) return Task.FromResult("No scheduled jobs.");
        var lines = jobs.Select(j =>
            $"[{j.Id}] {j.Name}: {j.Description} (every {j.Interval.TotalMinutes}min, active: {j.Active}, last: {j.LastRunAt?.ToString("g") ?? "never"})");
        return Task.FromResult(string.Join("\n", lines));
    }

    private async Task PublishDashboardChanged()
    {
        var dashboard = new ProjectDashboard
        {
            Tasks = durableState.Tasks.ToList(),
            Jobs = durableState.Schedules.Values.ToList(),
            Files = durableState.Files.Values.ToList(),
            GeneratedAt = DateTimeOffset.UtcNow
        };
        var markdown = DashboardRenderer.Render(dashboard);
        await PublishAsync("dashboard.changed", new Dictionary<string, object>
        {
            ["projectKey"] = this.GetPrimaryKeyString(),
            ["renderedMarkdown"] = markdown
        });
    }
}
