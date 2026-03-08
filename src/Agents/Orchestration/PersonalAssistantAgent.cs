using IAW.Agents.Infrastructure;
using IAW.Agents.Knowledge;
using IAW.Agents.Messages;
using IAW.Agents.Review;
using IAW.Core;
using IAW.Core.AI;
using IAW.Core.AI.Models;
using IAW.Core.Attributes;
using IAW.Core.Communication;
using IAW.Core.Registry;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace IAW.Agents.Orchestration;

[DevVisible("Orchestrator -- decomposes tasks, delegates to team")]
public class PersonalAssistantAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Sonnet46>] IChatClient chatClient,
    [Memory("history")] IDurableList<IAW.Core.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IPersonalAssistant,
      IReceiver<TaskCompletedMessage>,
      IReceiver<TaskFailedMessage>,
      IReceiver<DeploySucceededMessage>,
      IReceiver<ReviewCompletedMessage>
{
    protected override string DisplayName => "Personal Assistant";

    protected override string Instructions => """
        You are the Personal Assistant — the CEO of an AI engineering team.
        You receive user requests, decompose them into tasks, and delegate to your team.

        Core team (always available):
        - Roslyn (roslyn): C# code intelligence — type catalogs, syntax trees, pattern detection
        - DotNet (dot-net): .NET toolchain — build, test, format, publish
        - Reviewer (reviewer): code review and quality checks
        - SelfImprovement (self-improvement): analyzes metrics, proposes and executes code improvements
        - Deployer (deployer): release builds, deployment, git commits
        - Planning (planning): generates multi-step execution plans
        - Knowledge (knowledge): project conventions, architecture decisions, patterns
        - NuGet (nu-get): package management, dependency analysis
        - GitHub (git-hub): GitHub API — PRs, issues, releases

        Infrastructure (use indirectly via other agents, or directly for low-level ops):
        - FileSystem (file-system), Shell (shell), Git (git), Build (build)

        Use AssignTaskToAgent with the grain key in parentheses above.
        If you say you will hand off/delegate to an agent, you MUST actually call AssignTaskToAgent in that same turn.
        For planning/spec/design requests, delegate to Planning before giving the final answer.
        Never end a response with pending-action filler text (for example "now let me..." or a trailing colon).
        Do not provide progress narration unless you also provide concrete, completed output.
        Always report progress back to the user clearly and concisely.
        """;

    protected override AgentKind AgentKindValue => AgentKind.Static;

    protected override IReadOnlyList<AITool> DefineTools()
    {
        return
        [
            AIFunctionFactory.Create(AssignTaskToAgent, nameof(AssignTaskToAgent),
                "Assign a task to a specific agent by grain key"),
            AIFunctionFactory.Create(GetTeamStatusTool, nameof(GetTeamStatusTool),
                "Get the current status of all engineering team members"),
            AIFunctionFactory.Create(SpawnDynamicAgent, nameof(SpawnDynamicAgent),
                "Spawn a dynamic agent for parallel work"),
        ];
    }

    [Description("Assign a task to a specific agent by grain key")]
    private async Task<string> AssignTaskToAgent(
        [Description("Grain key of the target agent (e.g. 'roslyn', 'dot-net', 'reviewer', 'self-improvement', 'deployer')")] string agentKey,
        [Description("Description of the task")] string description,
        [Description("Optional file path for context")] string? filePath = null,
        CancellationToken ct = default)
    {
        var agent = ResolveAgent(agentKey);
        if (agent is null)
            return $"Unknown agent key: {agentKey}";

        var prompt = $"Task: {description}" + (filePath is not null ? $"\nFile: {filePath}" : "");
        var responseBuilder = new StringBuilder();
        var sawError = false;
        try
        {
            await foreach (var chunk in agent.GetResponseStream(prompt, ct))
            {
                responseBuilder.Append(chunk);
            }
        }
        catch (Exception ex)
        {
            sawError = true;
            responseBuilder.AppendLine(BuildSafeErrorMessage(ex));
        }

        var taskId = Guid.NewGuid().ToString("N")[..8];
        State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
            JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = "assigned" }));
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

    [Description("Get the current status of all engineering team members")]
    private async Task<string> GetTeamStatusTool()
    {
        var sb = new StringBuilder();
        var registry = GrainFactory.GetGrain<IAgentRegistryGrain>("global");
        var registrations = await registry.GetAllAsync();

        sb.AppendLine($"Registered agents ({registrations.Count}):");
        foreach (var reg in registrations.OrderBy(r => r.AgentType))
        {
            sb.AppendLine($"- {reg.DisplayName} [{reg.Kind}]: {reg.Description}");
            if (reg.Capabilities.Length > 0)
                sb.AppendLine($"  Capabilities: {string.Join(", ", reg.Capabilities)}");
        }

        var knownAgents = new (string Id, string Name, Func<IAgent> Resolve)[]
        {
            ("reviewer", "Reviewer", () => GrainFactory.GetGrain<IReviewer>("reviewer")),
            ("self-improvement", "SelfImprovement", () => GrainFactory.GetGrain<ISelfImprovement>("self-improvement")),
            ("deployer", "Deployer", () => GrainFactory.GetGrain<IDeployer>("deployer")),
            ("planning", "Planning", () => GrainFactory.GetGrain<IPlanning>("planning")),
            ("knowledge", "Knowledge", () => GrainFactory.GetGrain<IKnowledge>("knowledge")),
            ("build", "Build", () => GrainFactory.GetGrain<IBuild>("build")),
            ("git", "Git", () => GrainFactory.GetGrain<IGit>("git")),
            ("file-system", "FileSystem", () => GrainFactory.GetGrain<IFileSystem>("file-system")),
        };

        sb.AppendLine();
        sb.AppendLine("Agent state summary:");
        foreach (var (id, name, resolve) in knownAgents)
        {
            try
            {
                var agent = resolve();
                var agentState = await agent.GetState(default);
                sb.AppendLine($"- {name}: {agentState.Entries.Count} state entries");
            }
            catch
            {
                sb.AppendLine($"- {name}: unavailable");
            }
        }

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

    public async Task<MessageReceipt> ReceiveAsync(TaskCompletedMessage message, CancellationToken ct = default)
    {
        State[$"completed-{message.TaskId}"] = new StateEntry($"completed-{message.TaskId}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

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

    Task<bool> IReceiver<TaskCompletedMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    Task<bool> IReceiver<TaskFailedMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    Task<bool> IReceiver<DeploySucceededMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    Task<bool> IReceiver<ReviewCompletedMessage>.CanReceiveAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    private IAgent? ResolveAgent(string agentKey)
    {
        var baseAgents = new Dictionary<string, Func<IAgent>>(StringComparer.OrdinalIgnoreCase)
        {
            ["reviewer"] = () => GrainFactory.GetGrain<IReviewer>("reviewer"),
            ["self-improvement"] = () => GrainFactory.GetGrain<ISelfImprovement>("self-improvement"),
            ["deployer"] = () => GrainFactory.GetGrain<IDeployer>("deployer"),
            ["planning"] = () => GrainFactory.GetGrain<IPlanning>("planning"),
            ["notification"] = () => GrainFactory.GetGrain<INotification>("notification"),
            ["knowledge"] = () => GrainFactory.GetGrain<IKnowledge>("knowledge"),
            ["user"] = () => GrainFactory.GetGrain<IUser>("user"),
            ["file-system"] = () => GrainFactory.GetGrain<IFileSystem>("file-system"),
            ["shell"] = () => GrainFactory.GetGrain<IShell>("shell"),
            ["git"] = () => GrainFactory.GetGrain<IGit>("git"),
            ["build"] = () => GrainFactory.GetGrain<IBuild>("build"),
            ["aspire"] = () => GrainFactory.GetGrain<IAspire>("aspire"),
        };

        if (baseAgents.TryGetValue(agentKey, out var factory))
            return factory();

        return ResolveAgentByReflection(agentKey);
    }

    private IAgent? ResolveAgentByReflection(string agentKey)
    {
        var interfaceName = agentKey switch
        {
            "roslyn" => "IRoslyn",
            "dot-net" => "IDotNet",
            "nu-get" => "INuGet",
            "git-hub" => "IGitHub",
            _ => null
        };

        if (interfaceName is null)
            return null;

        var interfaceType = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t => t.IsInterface && t.Name == interfaceName && typeof(IAgent).IsAssignableFrom(t));

        if (interfaceType is null)
            return null;

        return GrainFactory.GetGrain(interfaceType, agentKey) as IAgent;
    }
}
