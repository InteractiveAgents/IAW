using System.ComponentModel;
using System.Text;
using Core.AI;
using Core.AI.Models;
using Core.Context;
using Core.Contracts;
using IAW.Agents.Orchestration;
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
    protected override string Instructions => GetTopicSlug() switch
    {
        "general" => """
            You are the general assistant for this workspace. Answer quick questions directly.
            You have awareness of all topics — give status updates when asked.

            CRITICAL: You CANNOT write code, create files, or run commands yourself.
            You MUST call tool functions to do any real work:
            - DelegateToAssistant: for simple tasks (build, review, git, single-agent work)
            - ExecuteWithCode: for complex tasks (loops, data processing, research, file generation).
              Pass the full plan as the argument. The CodeOrchestrator will generate and execute C# code.
            - Recall: to search past task results or documents

            NEVER generate code in your response. ALWAYS call the appropriate tool function.
            If a conversation goes deep into a specific domain, suggest the appropriate topic.
            """,
        "personal" => """
            You are the user's personal assistant. Remember preferences, personal facts,
            and casual conversation. Be warm and helpful. Use memories naturally.
            For technical work, suggest using a work topic instead.
            For tasks that require creating files, running commands, building code — use DelegateToAssistant.
            """,
        "iaw" => """
            You are the assistant for the IAW project. You have access to the Aspire agent
            which can check resource health, read logs, traces, and troubleshoot errors.

            ROUTING:
            - For simple tasks (build, review, check traces) — use DelegateToAssistant
            - For complex tasks (data processing, research, file generation) — use ExecuteWithCode
            - To find past work results or documents — use Recall
            """,
        "scheduled" => """
            You manage scheduled jobs and recurring tasks. Help the user create, list,
            and cancel scheduled jobs. Use ScheduleJobTool and CancelJobTool.
            Show the current schedule when asked.
            """,
        _ => """
            You are a project assistant. Help the user manage their project,
            answer questions, and coordinate tasks. Be concise and actionable.

            CRITICAL: You CANNOT write code, create files, or run commands yourself.
            You MUST call tool functions to do any real work:
            - DelegateToAssistant: for simple tasks (build, review, git, single-agent work)
            - ExecuteWithCode: for complex tasks (loops, data processing, research, file generation).
              Pass the full plan as the argument. The CodeOrchestrator will generate and execute C# code.
            - Recall: to search past task results or documents

            NEVER generate code in your response. ALWAYS call ExecuteWithCode or DelegateToAssistant instead.
            """
    };
    protected override string DisplayName => "Project";

    private IReadOnlyList<IAgentContextProvider>? _contextProviders;

    protected override IReadOnlyList<IAgentContextProvider> GetContextProviders()
    {
        if (_contextProviders is not null) return _contextProviders;

        var providers = new List<IAgentContextProvider>
        {
            new UserContextProvider(GrainFactory),
            new ProjectContextProvider(durableState.Tasks, durableState.Files, durableState.ProjectMeta),
            new TaskContextProvider(durableState.Tasks)
        };

        var qdrant = ServiceProvider.GetService<QdrantClient>();
        var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (qdrant is not null && embeddings is not null)
            providers.Add(new RAGContextProvider(qdrant, embeddings));

        if (qdrant is not null && embeddings is not null)
        {
            var userId = this.GetPrimaryKeyString().Split('/')[0];
            providers.Add(new TaskResultContextProvider(qdrant, embeddings, userId));
        }

        var memoryAgents = ServiceProvider.GetService<IReadOnlyList<IMemoryAgent>>();
        if (memoryAgents is not null && memoryAgents.Count > 0)
            providers.Add(new MemoryContextProvider(memoryAgents));

        _contextProviders = providers;
        return _contextProviders;
    }

    private string GetTopicSlug()
    {
        var key = this.GetPrimaryKeyString();
        var slashIndex = key.LastIndexOf('/');
        return slashIndex >= 0 ? key[(slashIndex + 1)..] : key;
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
            AIFunctionFactory.Create(DelegateToAssistant, nameof(DelegateToAssistant),
                "Delegate a complex task to the PersonalAssistant who can assign work to specialized agents (FileSystem, Shell, Build, Git, Roslyn, etc.)"),
            AIFunctionFactory.Create(ExecuteWithCode, nameof(ExecuteWithCode),
                "Execute a complex task via generated C# code. Use for tasks involving loops, data processing, multi-source research, file generation, or multi-step workflows."),
            AIFunctionFactory.Create(RecallTool, nameof(RecallTool),
                "Search past task results, conversations, and documents for relevant context"),
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

    [Description("Delegate a complex task to the PersonalAssistant engineering team")]
    private async Task<string> DelegateToAssistant(
        [Description("Full description of what needs to be done")] string taskDescription)
    {
        var assistant = GrainFactory.GetGrain<IPersonalAssistant>("personal-assistant");
        var sb = new StringBuilder();
        WriteToolProgress("\n\n---\nDelegating to engineering team...\n\n");
        await foreach (var chunk in assistant.GetResponseStream(taskDescription, CancellationToken.None))
        {
            sb.Append(chunk);
            WriteToolProgress(chunk);
        }
        WriteToolProgress("\n---\n");
        return sb.ToString();
    }

    [Description("Execute a complex task via generated C# code. " +
        "Provide: what the user wants, success metrics, and step-by-step plan.")]
    private async Task<string> ExecuteWithCode(
        [Description("Full plan: intent, success metrics, and steps")] string plan)
    {
        var orchestrator = GrainFactory.GetGrain<ICodeOrchestrator>("code-orchestrator");
        WriteToolProgress("\n\n---\nGenerating and executing code...\n\n");
        var result = await orchestrator.ExecuteCodeOrchestration(plan, CancellationToken.None);
        WriteToolProgress(result);
        WriteToolProgress("\n---\n");
        return result;
    }

    [Description("Search past task results, conversations, and documents")]
    private async Task<string> RecallTool(
        [Description("What to search for")] string query,
        [Description("Maximum results to return")] int maxResults = 5)
    {
        var qdrant = ServiceProvider.GetService<QdrantClient>();
        var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
        if (qdrant is null || embeddings is null) return "Search not available.";

        var userId = this.GetPrimaryKeyString().Split('/')[0];
        var collections = new[] { $"task-results-{userId}", $"project-{this.GetPrimaryKeyString().Replace("/", "-")}" };
        var results = new List<string>();

        var queryEmbedding = await embeddings.GenerateAsync([query]);
        var queryVector = queryEmbedding[0].Vector.ToArray();

        foreach (var collection in collections)
        {
            try
            {
                if (!await qdrant.CollectionExistsAsync(collection))
                    continue;
                var hits = await qdrant.SearchAsync(collection, queryVector, limit: (ulong)maxResults);
                results.AddRange(hits.Where(h => h.Score > 0.4f)
                    .Select(h => $"[{collection}] {h.Payload["text"]}"));
            }
            catch { }
        }

        if (results.Count == 0) return "No relevant results found.";
        return string.Join("\n\n", results.Take(maxResults));
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
            Tasks = durableState.Tasks.ToArray(),
            Jobs = durableState.Schedules.Values.ToArray(),
            Files = durableState.Files.Values.ToArray(),
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
        Task.FromResult<IReadOnlyList<ProjectTask>>(durableState.Tasks.ToArray());

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
            Tasks = durableState.Tasks.ToArray(),
            Jobs = durableState.Schedules.Values.ToArray(),
            Files = durableState.Files.Values.ToArray(),
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
