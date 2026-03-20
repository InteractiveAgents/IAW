using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Core;
using Core.AI;
using Core.AI.Models;
using Core.Context;
using Core.Contracts;
using Core.Registry;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;

namespace IAW.Agents.Orchestration;

[GrainType(IAWConstants.GrainTypes.Project)]
public class Project(
    [ProjectState] ProjectDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IProject
{
    public static string AgentDescription => "User-facing project assistant that manages tasks, scheduled jobs, and delegates work to specialized agents via code orchestration.";
    public static string[] AgentCapabilities => ["project", "tasks", "schedule", "orchestrate", "assistant", "recall"];

    protected override string Instructions => GetTopicSlug() switch
    {
        "general" => """
            You are the user's assistant. Be concise and direct. Use markdown formatting.

            TOOLS:
            - Execute: run any task via generated code (build, create files, search, deploy, etc.)
            - RequestApprovalTool: ask user to choose or confirm before acting
            - Recall: search past results and documents
            - ScheduleJobTool/CancelJobTool: manage recurring tasks
            - CompareModelsTool: send same prompt to multiple LLMs and compare speed/tokens/quality

            BEHAVIOR:
            - If you can answer from knowledge or memory, answer directly
            - If the request is clear, call Execute immediately
            - If ambiguous or risky, explain your plan, use RequestApprovalTool to confirm, then Execute
            - Never generate code in your response. Always use Execute.
            """,
        "personal" => """
            You are the user's personal assistant. Warm and helpful. Use markdown.
            Remember preferences and personal facts via memory.
            For any task requiring action, use Execute.
            For scheduling, use ScheduleJobTool.
            """,
        "iaw" => """
            You are the IAW project assistant. Use markdown.
            You can check Aspire resource health, read logs and traces.
            For any action (build, test, deploy, troubleshoot), use Execute.
            If ambiguous, confirm the plan first with RequestApprovalTool.
            """,
        "scheduled" => """
            You manage scheduled jobs and recurring tasks. Use markdown.
            Use ScheduleJobTool and CancelJobTool to manage jobs.
            Use Execute for one-off tasks.
            """,
        _ => """
            You are a project assistant. Be concise. Use markdown.

            TOOLS:
            - Execute: run any task via generated code
            - RequestApprovalTool: ask user to choose or confirm
            - Recall: search past results and documents
            - ScheduleJobTool/CancelJobTool: recurring tasks
            - CompareModelsTool: compare LLM responses side-by-side

            If the request is clear, call Execute immediately.
            If ambiguous, explain your plan and use RequestApprovalTool first.
            Never generate code in your response.
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
            AIFunctionFactory.Create(Execute, nameof(Execute),
                "Execute a task by generating and running C# code that calls agent interfaces directly"),
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
            AIFunctionFactory.Create(RecallTool, nameof(RecallTool),
                "Search past task results, conversations, and documents for relevant context"),
            AIFunctionFactory.Create(CompareModelsTool, nameof(CompareModelsTool),
                "Send the same prompt to multiple LLM models in parallel and compare their responses, speed, and token usage."),
        ];
    }

    [Description("Execute a task by generating and running C# code. " +
        "The code connects to the agent cluster and calls agents directly.")]
    private async Task<string> Execute(
        [Description("What to do, step by step")] string plan)
    {
        var orchestrator = GrainFactory.Get<ICodeOrchestrator>();
        var result = await orchestrator.ExecuteCodeOrchestration(plan, CancellationToken.None);
        return result;
    }

    [Description("Request user approval with a question and a set of options")]
    private async Task<string> RequestApprovalTool(
        [Description("The question to ask the user")] string question,
        [Description("Available options for the user to choose from")] string[] options)
    {
        var approvalId = Guid.NewGuid().ToString("N")[..8];
        await PublishAsync(IAWConstants.Events.ApprovalRequested, new Dictionary<string, object>
        {
            ["approvalId"] = approvalId,
            ["question"] = question,
            ["options"] = options,
            ["projectSlug"] = this.GetPrimaryKeyString()
        });
        return $"Approval requested (id: {approvalId}). Waiting for user response.";
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

    public new async Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct)
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
        await ScheduleRecurringJob(job.Id, interval, job.Description, ct);
        await PublishDashboardChanged();
        return job;
    }

    public new async Task CancelJob(string jobId, CancellationToken ct)
    {
        if (!durableState.Schedules.TryGetValue(jobId, out var job))
            throw new KeyNotFoundException($"Job {jobId} not found");

        durableState.Schedules[jobId] = job with { Active = false };
        await base.CancelJob(jobId, ct);
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

    protected override async Task OnScheduledJobDueAsync(ScheduledJobItem scheduledJob, CancellationToken ct)
    {
        if (!durableState.Schedules.TryGetValue(scheduledJob.Name, out var job) || !job.Active)
        {
            await base.OnScheduledJobDueAsync(scheduledJob, ct);
            return;
        }

        var response = await GetResponse(scheduledJob.Prompt, ct);
        durableState.Schedules[job.Id] = job with
        {
            LastRunAt = DateTimeOffset.UtcNow,
            NextRunAt = DateTimeOffset.UtcNow + job.Interval,
            LastResult = response
        };
        await PublishDashboardChanged();

        await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, object>
        {
            ["projectKey"] = this.GetPrimaryKeyString(),
            ["jobName"] = job.Name,
            ["result"] = response
        });
    }

    [Description("Schedule a recurring job. Automatically replaces any existing job with the same name.")]
    private async Task<string> ScheduleJobTool(
        [Description("Job name")] string name,
        [Description("Interval in minutes between runs")] int intervalMinutes,
        [Description("What the job should do each run")] string description)
    {
        var existing = durableState.Schedules.Values
            .Where(j => j.Active && j.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var old in existing)
            await CancelJob(old.Id, CancellationToken.None);

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var job = await ScheduleJob(name, interval, description, CancellationToken.None);
        var replaced = existing.Count > 0 ? $" (replaced {existing.Count} existing)" : "";
        return $"Job '{job.Id}' scheduled: {job.Name} — runs every {intervalMinutes} minutes{replaced}";
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

    [Description("Compare multiple LLM models by sending the same prompt to each in parallel. Returns responses with timing and token usage.")]
    private async Task<string> CompareModelsTool(
        [Description("The prompt to send to all models")] string prompt,
        [Description("Model names to compare (e.g. 'gpt54mini,sonnet46,claude45haiku'). Use 'all' for all configured LLM agents.")] string models = "all")
    {
        var registry = GrainFactory.GetGrain<IAgentRegistry>("global");
        var allAgents = await registry.GetAllAsync(CancellationToken.None);

        var llmAgentType = typeof(LlmAgentBase);
        var llmRecords = allAgents.Where(r =>
        {
            var implementor = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
                .FirstOrDefault(t => t is { IsAbstract: false, IsClass: true }
                    && t.Name == r.AgentType && llmAgentType.IsAssignableFrom(t));
            return implementor is not null;
        }).ToList();

        if (!models.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var requested = models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(m => m.ToLowerInvariant().Replace("-", "").Replace(".", "").Replace(" ", ""))
                .ToHashSet();
            llmRecords = llmRecords.Where(r => requested.Contains(r.AgentType.Replace("Agent", "").ToLowerInvariant())).ToList();
        }

        if (llmRecords.Count == 0)
            return $"No matching LLM agents found. Available: {string.Join(", ", allAgents.Select(r => r.AgentType))}";

        var compareScope = $"compare-{Guid.NewGuid():N}";
        var interfaceTypeCache = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t.IsInterface && typeof(IAgent).IsAssignableFrom(t) && t != typeof(IAgent))
            .ToDictionary(t => t.Name, t => t);

        var tasks = llmRecords.Select(async record =>
        {
            if (!interfaceTypeCache.TryGetValue(record.InterfaceName, out var interfaceType))
                return new { Model = record.DisplayName, Response = "", DurationMs = 0L, InputTokens = 0L, OutputTokens = 0L, TotalTokens = 0L, Error = (string?)$"Interface {record.InterfaceName} not found" };

            var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{compareScope}/{interfaceType.Name}");
            var sw = Stopwatch.StartNew();
            try
            {
                var response = await agent.GetResponse(prompt, CancellationToken.None);
                sw.Stop();
                var usage = await agent.GetLastUsage(CancellationToken.None);
                return new
                {
                    Model = record.DisplayName,
                    Response = response,
                    DurationMs = sw.ElapsedMilliseconds,
                    InputTokens = usage?.InputTokens ?? 0,
                    OutputTokens = usage?.OutputTokens ?? 0,
                    TotalTokens = usage?.TotalTokens ?? 0,
                    Error = (string?)null
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new
                {
                    Model = record.DisplayName,
                    Response = "",
                    DurationMs = sw.ElapsedMilliseconds,
                    InputTokens = 0L,
                    OutputTokens = 0L,
                    TotalTokens = 0L,
                    Error = (string?)ex.Message
                };
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var sb = new StringBuilder();
        sb.AppendLine($"## Model Comparison — {results.Length} models");
        sb.AppendLine();
        sb.AppendLine("| Model | Duration | Input Tokens | Output Tokens | Total Tokens |");
        sb.AppendLine("|-------|----------|-------------|--------------|-------------|");

        foreach (var r in results.OrderBy(r => r.DurationMs))
        {
            if (r.Error is not null)
                sb.AppendLine($"| {r.Model} | ERROR | — | — | {r.Error} |");
            else
                sb.AppendLine($"| {r.Model} | {r.DurationMs}ms | {r.InputTokens} | {r.OutputTokens} | {r.TotalTokens} |");
        }

        sb.AppendLine();
        foreach (var r in results.OrderBy(r => r.DurationMs))
        {
            sb.AppendLine($"### {r.Model}");
            if (r.Error is not null)
                sb.AppendLine($"Error: {r.Error}");
            else
                sb.AppendLine(r.Response);
            sb.AppendLine();
        }

        return sb.ToString();
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
        await PublishAsync(IAWConstants.Events.DashboardChanged, new Dictionary<string, object>
        {
            ["projectKey"] = this.GetPrimaryKeyString(),
            ["renderedMarkdown"] = markdown
        });
    }
}
