using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Communication;
using Core.Context;
using Core.Contracts;
using Core.Registry;
using IAW.Agents.Infrastructure;
using IAW.Agents.Knowledge;
using IAW.Agents.Memory;
using IAW.Agents.Messages;
using IAW.Agents.Review;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Orchestration;

public class PersonalAssistantAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient),
      IPersonalAssistant,
      IReceiver<TaskCompletedMessage>,
      IReceiver<TaskFailedMessage>,
      IReceiver<DeploySucceededMessage>,
      IReceiver<ReviewCompletedMessage>
{
    protected override string DisplayName => "Personal Assistant";

    protected override string Instructions => """
        You are the Personal Assistant — the primary interface between the user and the IAW engineering team.

        CORE BEHAVIOR:
        - You are concise, direct, and action-oriented. Never explain what you're "about to do" — just do it.
        - When the user asks a question you can answer from memory or context, answer directly.
        - When the user asks you to DO something (build, fix, deploy, review, write code), delegate immediately.
        - Always report results, not intentions. Say "Build succeeded with 0 warnings" not "Let me run the build for you."

        DELEGATION RULES:
        - For quick operations (file reads, simple commands, status checks): use AssignTaskToAgent (synchronous, waits for result)
        - For long-running work (full builds, code reviews, multi-step plans, deployments): use AssignBackgroundTask (async, returns immediately)
        - When delegating, give the target agent a clear, specific prompt. Include file paths, expected outcomes, and constraints.
        - If a delegated task fails, try ONE retry with a refined prompt before reporting the failure to the user.

        MEMORY:
        - When the user shares personal facts (name, birthday, preferences, project goals), call RememberFact immediately.
        - When your context includes memories, use them naturally without saying "according to my memory."
        - When the user asks "do you remember...", call RecallMemories and answer based on results.

        YOUR TEAM (use AssignTaskToAgent/AssignBackgroundTask with the grain key shown):
        - Roslyn (roslyn): C# code intelligence — syntax trees, types, patterns, dependency graphs
        - DotNet (dot-net): .NET toolchain — build, test, format, publish
        - Reviewer (reviewer): code quality review — naming, patterns, correctness
        - SelfImprovement (self-improvement): metrics analysis, codebase quality improvements
        - Deployer (deployer): release builds, deployment, git commits
        - Planning (planning): multi-step execution plans for complex tasks
        - Knowledge (knowledge): project conventions, architecture decisions, patterns
        - NuGet (nu-get): package management — search, install, update, audit
        - GitHub (git-hub): GitHub API — PRs, issues, releases, actions
        - Shell (shell): shell command execution
        - FileSystem (file-system): file read/write/search/list
        - Git (git): version control — status, diff, branch, merge
        - Build (build): compilation and test execution
        - Aspire (aspire): Aspire service orchestration and monitoring
        - Notification (notification): alert routing and notification delivery
        - User (user): user profile context and preferences

        CONSTRAINTS:
        - If you say you will delegate, you MUST call AssignTaskToAgent or AssignBackgroundTask in the same turn.
        - Never end a response with a trailing action ("now let me..."). If there's more to do, do it; if not, stop.
        - When multiple tasks are independent, use AssignBackgroundTask for each and report them all.
        """;

    protected override AgentKind AgentKindValue => AgentKind.Static;

    protected override IReadOnlyList<global::Core.Context.IAgentContextProvider> GetContextProviders() =>
    [
        new MemoryContextProvider([
            GrainFactory.GetGrain<IUserMemory>("user-memory"),
            GrainFactory.GetGrain<IProjectMemory>("project-memory"),
            GrainFactory.GetGrain<IEpisodeMemory>("episode-memory"),
        ])
    ];

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(AssignTaskToAgent, nameof(AssignTaskToAgent),
                "Assign a task to a specific agent by grain key (synchronous — waits for result)"),
            AIFunctionFactory.Create(AssignBackgroundTask, nameof(AssignBackgroundTask),
                "Assign a long-running background task to an agent (returns immediately, agent works asynchronously)"),
            AIFunctionFactory.Create(CheckTaskStatus, nameof(CheckTaskStatus),
                "Check the status of a previously assigned background task"),
            AIFunctionFactory.Create(GetTeamStatusTool, nameof(GetTeamStatusTool),
                "Get the current status of all engineering team members"),
            AIFunctionFactory.Create(SpawnDynamicAgent, nameof(SpawnDynamicAgent),
                "Spawn a dynamic agent for parallel work"),
            AIFunctionFactory.Create(RememberFact, nameof(RememberFact),
                "Store an important fact about the user for future conversations"),
            AIFunctionFactory.Create(RecallMemories, nameof(RecallMemories),
                "Search stored memories for information about a topic"),
        ];
    }

    // -- Tools ----------------------------------------------------------------

    [Description("Assign a task to a specific agent by grain key (synchronous — waits for result)")]
    private async Task<string> AssignTaskToAgent(
        [Description("Grain key of the target agent (e.g. 'roslyn', 'dot-net', 'reviewer', 'shell', 'file-system')")] string agentKey,
        [Description("Description of the task")] string description,
        [Description("Optional file path for context")] string? filePath = null,
        CancellationToken ct = default)
    {
        var agent = ResolveAgent(agentKey);
        if (agent is null)
            return $"Unknown agent key: {agentKey}. Available: {string.Join(", ", AgentInterfaces.Keys)}";

        var prompt = $"Task: {description}" + (filePath is not null ? $"\nFile: {filePath}" : "");
        var responseBuilder = new StringBuilder();
        var sawError = false;
        WriteToolProgress($"\n[{agentKey}]: ");
        try
        {
            await foreach (var chunk in agent.GetResponseStream(prompt, ct))
            {
                responseBuilder.Append(chunk);
                WriteToolProgress(chunk);
            }
        }
        catch (Exception ex)
        {
            sawError = true;
            responseBuilder.AppendLine(BuildSafeErrorMessage(ex));
        }
        WriteToolProgress("\n");

        var taskId = Guid.NewGuid().ToString("N")[..8];
        State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
            JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = sawError ? "failed" : "completed" }));
        await WriteStateAsync(ct);

        await PublishAsync("task.assigned", new Dictionary<string, object>
        {
            ["TaskId"] = taskId,
            ["AssignedTo"] = agentKey,
            ["Description"] = description
        }, ct);

        var result = responseBuilder.Length > 0 ? responseBuilder.ToString() : "[Agent acknowledged]";
        if (sawError)
            return $"Task assigned to {agentKey} (ID: {taskId}), but the delegated agent reported an error: {result}";

        return $"Task assigned to {agentKey} (ID: {taskId}). Response: {result}";
    }

    [Description("Assign a long-running background task (returns immediately, agent works asynchronously)")]
    private async Task<string> AssignBackgroundTask(
        [Description("Grain key of the target agent")] string agentKey,
        [Description("Description of the task")] string description,
        CancellationToken ct = default)
    {
        var agent = ResolveAgent(agentKey);
        if (agent is null)
            return $"Unknown agent key: {agentKey}. Available: {string.Join(", ", AgentInterfaces.Keys)}";

        var taskId = Guid.NewGuid().ToString("N")[..8];
        State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
            JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = "running" }));
        await WriteStateAsync(ct);

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await agent.GetResponse(description, CancellationToken.None);
                State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
                    JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = "completed", Result = TruncateResult(result) }));
                await WriteStateAsync(CancellationToken.None);
                await PublishAsync("task.completed", new Dictionary<string, object>
                {
                    ["TaskId"] = taskId, ["AssignedTo"] = agentKey, ["Result"] = TruncateResult(result)
                }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
                    JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = "failed", Error = ex.Message }));
                await WriteStateAsync(CancellationToken.None);
                await PublishAsync("task.failed", new Dictionary<string, object>
                {
                    ["TaskId"] = taskId, ["AssignedTo"] = agentKey, ["Error"] = ex.Message
                }, CancellationToken.None);
            }
        });

        return $"Background task {taskId} assigned to {agentKey}. Use CheckTaskStatus('{taskId}') to check progress.";
    }

    [Description("Check the status of a previously assigned task")]
    private Task<string> CheckTaskStatus(
        [Description("Task ID to check")] string taskId)
    {
        var key = $"task-{taskId}";
        if (!State.TryGetValue(key, out var entry))
            return Task.FromResult($"Task {taskId} not found.");
        return Task.FromResult(entry.Value.ToString() ?? "Unknown status");
    }

    [Description("Get the current status of all engineering team members")]
    private async Task<string> GetTeamStatusTool()
    {
        var sb = new StringBuilder();
        var registry = GrainFactory.GetGrain<IAgentRegistryGrain>("global");
        var registrations = await registry.GetAllAsync();

        sb.AppendLine($"Registered agents ({registrations.Count}):");
        foreach (var reg in registrations.OrderBy(r => r.AgentType))
            sb.AppendLine($"- {reg.DisplayName} [{reg.Kind}]: {reg.Description}");

        sb.AppendLine();
        sb.AppendLine("Active tasks:");
        var activeTasks = State.Where(kvp => kvp.Key.StartsWith("task-")).Take(10);
        foreach (var kvp in activeTasks)
            sb.AppendLine($"- {kvp.Key}: {kvp.Value.Value}");

        if (!activeTasks.Any())
            sb.AppendLine("- No active tasks");

        return sb.ToString();
    }

    [Description("Spawn a dynamic agent with a specific purpose")]
    private async Task<string> SpawnDynamicAgent(
        [Description("Display name for the agent")] string displayName,
        [Description("System prompt describing what the agent should do")] string systemPrompt)
    {
        var agentId = $"dynamic-{Guid.NewGuid():N}"[..16];
        var agent = GrainFactory.GetGrain<IDynamicAgent>(agentId);
        await agent.ConfigureAsync(new AgentConfiguration(displayName, systemPrompt, [], null, null), default);
        return $"Dynamic agent spawned: {agentId} ({displayName})";
    }

    [Description("Store an important fact about the user (birthday, preferences, name, etc.) for future conversations")]
    private async Task<string> RememberFact(
        [Description("The fact to remember (e.g. 'User birthday is March 15')")] string fact,
        CancellationToken ct = default)
    {
        var userMemory = GrainFactory.GetGrain<IUserMemory>("user-memory");
        await userMemory.ObserveAsync(fact, "personal-assistant", ct);
        return $"Remembered: {fact}";
    }

    [Description("Search stored memories for information about a topic")]
    private async Task<string> RecallMemories(
        [Description("What to search for (e.g. 'birthday', 'preferences')")] string query,
        CancellationToken ct = default)
    {
        var userMemory = GrainFactory.GetGrain<IUserMemory>("user-memory");
        var results = await userMemory.SearchAsync(query, 5, ct);
        if (results.Count == 0)
            return "No memories found for that topic.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} memories:");
        foreach (var entry in results)
            sb.AppendLine($"- {entry.Content} (stored {entry.CreatedAt:yyyy-MM-dd})");
        return sb.ToString();
    }

    // -- Receivers ------------------------------------------------------------

    public async Task<MessageReceipt> ReceiveAsync(TaskCompletedMessage message, CancellationToken ct = default)
    {
        State[$"completed-{message.TaskId}"] = new StateEntry($"completed-{message.TaskId}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        var episodeMemory = GrainFactory.GetGrain<IEpisodeMemory>("episode-memory");
        await episodeMemory.ObserveAsync($"Completed task {message.TaskId} by {message.CompletedBy}: {TruncateResult(message.Result)}", "task-completion", ct);

        await PublishAsync("task.completed", new Dictionary<string, object>
        {
            ["TaskId"] = message.TaskId,
            ["CompletedBy"] = message.CompletedBy,
            ["Result"] = message.Result
        }, ct);

        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
    }

    public async Task<MessageReceipt> ReceiveAsync(TaskFailedMessage message, CancellationToken ct = default)
    {
        State[$"failed-{message.TaskId}"] = new StateEntry($"failed-{message.TaskId}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        await PublishAsync("task.failed", new Dictionary<string, object>
        {
            ["TaskId"] = message.TaskId,
            ["FailedBy"] = message.FailedBy,
            ["Error"] = message.Error
        }, ct);

        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
    }

    public async Task<MessageReceipt> ReceiveAsync(DeploySucceededMessage message, CancellationToken ct = default)
    {
        State[$"deployed-{message.TaskId}"] = new StateEntry($"deployed-{message.TaskId}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        await PublishAsync("deploy.completed", new Dictionary<string, object>
        {
            ["TaskId"] = message.TaskId,
            ["ResourceName"] = message.ResourceName
        }, ct);

        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
    }

    public async Task<MessageReceipt> ReceiveAsync(ReviewCompletedMessage message, CancellationToken ct = default)
    {
        State[$"review-{message.TaskId}"] = new StateEntry($"review-{message.TaskId}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null);
    }

    Task<bool> IReceiver<TaskCompletedMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    Task<bool> IReceiver<TaskFailedMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    Task<bool> IReceiver<DeploySucceededMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    Task<bool> IReceiver<ReviewCompletedMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    // -- Public interface -----------------------------------------------------

    public Task<string> GetTeamStatusAsync(CancellationToken ct = default)
        => GetTeamStatusTool();

    public Task<string[]> GetActiveTasksAsync(CancellationToken ct = default)
    {
        var tasks = State
            .Where(kvp => kvp.Key.StartsWith("task-"))
            .Select(kvp => kvp.Value.Value.ToString() ?? "")
            .ToArray();
        return Task.FromResult(tasks);
    }

    // -- Agent resolution -----------------------------------------------------

    private static readonly Dictionary<string, Type> AgentInterfaces = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reviewer"] = typeof(IReviewer),
        ["self-improvement"] = typeof(ISelfImprovement),
        ["deployer"] = typeof(IDeployer),
        ["planning"] = typeof(IPlanning),
        ["notification"] = typeof(INotificationAgent),
        ["knowledge"] = typeof(IKnowledge),
        ["user"] = typeof(IUser),
        ["file-system"] = typeof(IFileSystem),
        ["shell"] = typeof(IShell),
        ["git"] = typeof(IGit),
        ["build"] = typeof(IBuild),
        ["aspire"] = typeof(IAspire),
    };

    // CSharp agent interfaces resolved at runtime to avoid circular project reference
    private static readonly Dictionary<string, string> CSharpAgentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["roslyn"] = "IAW.Agents.CSharp.IRoslyn, IAW.Agents.CSharp",
        ["dot-net"] = "IAW.Agents.CSharp.IDotNet, IAW.Agents.CSharp",
        ["nu-get"] = "IAW.Agents.CSharp.INuGet, IAW.Agents.CSharp",
        ["git-hub"] = "IAW.Agents.CSharp.IGitHub, IAW.Agents.CSharp",
    };

    private IAgent? ResolveAgent(string agentKey)
    {
        if (AgentInterfaces.TryGetValue(agentKey, out var interfaceType))
            return GrainFactory.GetGrain(interfaceType, agentKey) as IAgent;

        if (CSharpAgentTypes.TryGetValue(agentKey, out var typeName))
        {
            var type = Type.GetType(typeName);
            if (type is not null)
                return GrainFactory.GetGrain(type, agentKey) as IAgent;
        }

        return null;
    }

    private static string TruncateResult(string result, int maxLength = 500)
        => result.Length <= maxLength ? result : result[..maxLength] + "... [truncated]";
}
