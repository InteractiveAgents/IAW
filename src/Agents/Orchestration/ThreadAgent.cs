using Core;
using Core.Context;
using Core.Contracts;
using Core.Contracts.Security;
using Core.Registry;
using Core.UI;
using IAW.Agents.Security;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using AgentResponse = global::Core.UI.AgentResponse;

namespace IAW.Agents.Orchestration;

[GrainType(IAWConstants.GrainTypes.Thread)]
public class ThreadAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    ILogger<ThreadAgent> logger)
    : Agent<IThread>(durableState, chatClient), IThread
{
    private const string CallbackPrefix = "cb:";
    private const string DigestJobPrefix = "digest:";
    private readonly List<MediaPart> _pendingDeliveries = [];

    protected override int MaxHistoryMessages => 20;

    private IReadOnlyList<IAgentContextProvider>? _contextProviders;

    protected override IReadOnlyList<IAgentContextProvider> GetContextProviders()
    {
        if (_contextProviders is not null) return _contextProviders;

        var providers = new List<IAgentContextProvider>
        {
            new UserContextProvider(GrainFactory)
        };

        var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();

        var qdrant = ServiceProvider.GetService<QdrantClient>();
        if (qdrant is not null && embeddings is not null)
            providers.Add(new RAGContextProvider(qdrant, embeddings));

        if (embeddings is not null)
            providers.Add(new AgentRoutingContextProvider(GrainFactory, embeddings));

        var memoryAgents = ServiceProvider.GetService<IReadOnlyList<IMemoryAgent>>();
        if (memoryAgents is not null && memoryAgents.Count > 0)
            providers.Add(new MemoryContextProvider(memoryAgents));

        _contextProviders = providers;
        return _contextProviders;
    }

    protected override IReadOnlyList<AITool> DefineAdditionalTools()
    {
        return [
            AIFunctionFactory.Create(SendToAgentAsync, "SendToAgent",
                "Delegate a task to a specialized agent from [Available agents for this request]. " +
                "Use the agent's DisplayName. The agent has its own LLM and tools — never do these tasks yourself. " +
                "Include FULL request with all paths and details."),

            AIFunctionFactory.Create(OrchestrateAsync, "Orchestrate",
                "For complex multi-step tasks requiring coordination across 3+ agents. " +
                "NOT needed for single-agent tasks — use SendToAgent instead."),

            CreateProposeOptionsTool(),

            AIFunctionFactory.Create(AddApproverPolicyAsync, "AddApproverPolicy",
                "Teach the Approver a new permission rule in natural language. Use when the user says things like " +
                "'don't ask me about builds anymore' or 'always allow git status'. " +
                "scope must be 'Thread' (only this conversation), 'User' (all conversations), or 'Once' (single use)."),

            AIFunctionFactory.Create(RemoveApproverPolicyAsync, "RemoveApproverPolicy",
                "Remove a previously-stored approval policy. Pass a natural-language description of which policy to remove; " +
                "the Approver will semantically match it."),

            AIFunctionFactory.Create(ListApproverPoliciesAsync, "ListApproverPolicies",
                "List all stored approval policies the Approver has learned so far for this user.")
        ];
    }

    public Task<IReadOnlyList<UIPart>> GetPendingUIHints(CancellationToken ct = default)
        => Task.FromResult(DrainPendingUIHints());

    [Description("Store a natural-language approval policy")]
    async Task<string> AddApproverPolicyAsync(
        [Description("Scope: Once, Thread, or User")] string scope,
        [Description("The policy rule in natural language")] string rule,
        CancellationToken ct = default)
    {
        var userId = ExtractUserId();
        if (userId is null) return "No user context available for policy storage.";

        var approver = GrainFactory.GetGrain<IApprover>(userId);
        return await approver.AddPolicy(scope, this.GetPrimaryKeyString(), rule, ct);
    }

    [Description("Remove a previously-stored approval policy matched by a natural-language query")]
    async Task<string> RemoveApproverPolicyAsync(
        [Description("Short description of which policy to remove")] string query,
        CancellationToken ct = default)
    {
        var userId = ExtractUserId();
        if (userId is null) return "No user context available.";

        var approver = GrainFactory.GetGrain<IApprover>(userId);
        return await approver.RemovePolicy(query, ct);
    }

    [Description("List all approval policies learned for the current user")]
    async Task<string> ListApproverPoliciesAsync(CancellationToken ct = default)
    {
        var userId = ExtractUserId();
        if (userId is null) return "No user context available.";

        var approver = GrainFactory.GetGrain<IApprover>(userId);
        var policies = await approver.ListPolicies(ct);
        if (policies.Count == 0)
            return "No policies stored yet.";

        var sb = new StringBuilder();
        foreach (var policy in policies)
            sb.AppendLine($"- [{policy.Scope}] {policy.Rule}");
        return sb.ToString().TrimEnd();
    }

    string? ExtractUserId()
    {
        var key = this.GetPrimaryKeyString();
        var slashIndex = key.IndexOf('/');
        if (slashIndex > 0) return key[..slashIndex];
        return long.TryParse(key, out _) ? key : null;
    }

    private async Task<string> SendToAgentAsync(string agentName, string request, CancellationToken ct = default)
    {
        logger.LogInformation("SendToAgent: {Agent} for: {Request}",
            agentName, request[..Math.Min(80, request.Length)]);

        var interfaceType = AgentInterfaceResolver.ResolveByDisplayName(agentName)
                         ?? AgentInterfaceResolver.Resolve(agentName);
        if (interfaceType is null)
        {
            var registry = GrainFactory.GetGrain<IAgentRegistry>("global");
            var all = await registry.GetAllAsync(ct);
            var names = string.Join(", ", all.Select(r => r.DisplayName).Where(n => n.Length > 0).Order());
            return $"Unknown agent: {agentName}. Available: {names}.";
        }

        var threadId = this.GetPrimaryKeyString();
        var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");

        var workspace = GetWorkspacePath();
        if (workspace is not null)
            await agent.SetWorkspace(workspace, ct);

        var enrichedRequest = workspace is not null
            ? $"[Workspace: {workspace}]\n{request}"
            : request;

        try
        {
            var response = await agent.GetRichResponse(enrichedRequest, ct);
            var text = string.Join("\n", response.Parts.OfType<TextPart>().Select(p => p.Content));
            _pendingDeliveries.AddRange(response.Parts.OfType<MediaPart>());

            return text.Length > 4000
                ? text[..4000] + "\n...(truncated)"
                : text;
        }
        catch (OperationCanceledException)
        {
            return $"Agent {agentName} timed out. Try a simpler request or a different agent.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendToAgent: {Agent} failed", agentName);
            return $"Agent {agentName} failed: {ex.Message}\nTry a different agent or rephrase the request.";
        }
    }

    private async Task<string> OrchestrateAsync(string request, CancellationToken ct = default)
    {
        var taskId = $"dlg-{Guid.NewGuid().ToString("N")[..8]}";
        logger.LogInformation("Orchestrate: executing {TaskId} for: {Request}",
            taskId, request[..Math.Min(80, request.Length)]);

        return await ExecuteDelegation(taskId, request, ct);
    }

    private async Task<string> ExecuteSelection(SelectionResult selection, string request, CancellationToken ct)
    {
        var threadId = this.GetPrimaryKeyString();
        var lastUserMsg = History.LastOrDefault(m => m.Role == "user");
        var userMessage = lastUserMsg?.Text ?? request;

        if (selection.SelectedAgents.Count == 1)
        {
            var agentInterfaceName = selection.SelectedAgents[0];
            var interfaceType = AgentInterfaceResolver.Resolve(agentInterfaceName);
            if (interfaceType is null)
                return $"Could not resolve agent: {agentInterfaceName}";

            var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");

            var workspace = GetWorkspacePath();
            if (workspace is not null)
                await agent.SetWorkspace(workspace, ct);

            return await agent.GetResponse(request, ct);
        }

        var orchestrator = GrainFactory.Get<ICodeOrchestrator>(threadId);
        var selectorPlan = selection.Plan ?? $"Agents: {string.Join(", ", selection.SelectedAgents)}";
        var plan = $"USER REQUEST: {userMessage}\n\nPLAN:\n{selectorPlan}";
        var result = await orchestrator.ExecuteCodeOrchestration(plan, selection.SelectedAgents, threadId, ct);
        return JsonSerializer.Serialize(result);
    }

    private static string FormatClarificationResponse(SelectionResult result)
    {
        if (result.Questions is null or { Count: 0 })
            return "I need more information to proceed. Could you clarify your request?";

        var sb = new global::System.Text.StringBuilder("I need some clarification:\n\n");
        foreach (var q in result.Questions)
        {
            sb.AppendLine($"- {q.Text}");
            if (q.Options is { Count: > 0 })
                sb.AppendLine($"  Options: {string.Join(", ", q.Options)}");
        }
        return sb.ToString();
    }

    protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
    {
        if (job.Prompt.StartsWith(DigestJobPrefix))
        {
            var taskId = job.Prompt[DigestJobPrefix.Length..];
            await ExecuteDigestAsync(taskId, ct);
            return;
        }

        if (!job.Prompt.StartsWith(IAWConstants.DelegationPrefix))
        {
            await base.OnScheduledJobDueAsync(job, ct);
            return;
        }

        var request = job.Prompt[IAWConstants.DelegationPrefix.Length..];
        var result = await ExecuteDelegation(job.Name, request, ct);

        var updated = job with { LastRunAt = DateTimeOffset.UtcNow, LastResult = result };
        ScheduledJobs[job.Name] = updated;
    }

    private async Task<string> ExecuteDelegation(string taskId, string request, CancellationToken ct)
    {
        logger.LogInformation("Delegation: executing {TaskId} for: {Request}",
            taskId, request[..Math.Min(80, request.Length)]);

        string delegationResult;
        using var selectorTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var selectorLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, selectorTimeout.Token);
        try
        {
            var selector = GrainFactory.Get<IAgentSelector>();
            var selection = await selector.SelectAsync(request, selectorLinked.Token);

            logger.LogInformation("Delegation: selector returned Status={Status}, Agents=[{Agents}]",
                selection.Status, string.Join(",", selection.SelectedAgents));

            delegationResult = selection.Status switch
            {
                SelectionStatus.Ready => await ExecuteSelection(selection, request, ct),
                SelectionStatus.CannotHandle => selection.Plan ?? "The agent system cannot handle this request.",
                SelectionStatus.NeedsClarification => FormatClarificationResponse(selection),
                _ => "Unexpected selection status."
            };
        }
        catch (OperationCanceledException) when (selectorTimeout.IsCancellationRequested)
        {
            logger.LogWarning("Delegation: selector timed out for {TaskId}", taskId);
            delegationResult = "Delegation timed out during agent selection. Please try again.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delegation: FAILED {TaskId}", taskId);
            delegationResult = $"Delegation failed: {ex.GetType().Name}: {ex.Message}";
        }

        logger.LogInformation("Delegation: completed {TaskId}, result length: {Length}",
            taskId, delegationResult.Length);

        var safeResult = TruncateOrchestrationResultSafely(delegationResult);

        await PublishAsync(IAWConstants.Events.JobCompleted, new Dictionary<string, string>
        {
            [IAWConstants.PayloadKeys.ProjectKey] = this.GetPrimaryKeyString(),
            [IAWConstants.PayloadKeys.JobName] = taskId,
            [IAWConstants.PayloadKeys.Result] = safeResult
        }, CancellationToken.None);

        return delegationResult;
    }

    private static string TruncateOrchestrationResultSafely(string resultPayload)
    {
        const int maxLength = 4000;
        if (resultPayload.Length <= maxLength)
            return resultPayload;

        // Try to truncate ErrorDetail inside the JSON structure before re-serializing
        try
        {
            var parsed = JsonSerializer.Deserialize<OrchestrationResult>(resultPayload);
            if (parsed is not null)
            {
                var truncatedError = parsed.ErrorDetail is { Length: > 500 }
                    ? parsed.ErrorDetail[..500] + "...(truncated)"
                    : parsed.ErrorDetail;
                var truncatedSummary = parsed.Summary is { Length: > 1000 }
                    ? parsed.Summary[..1000] + "...(truncated)"
                    : parsed.Summary;
                var compact = parsed with { ErrorDetail = truncatedError, Summary = truncatedSummary };
                return JsonSerializer.Serialize(compact);
            }
        }
        catch (JsonException) { }

        // Non-JSON result: truncate the plain text safely
        return resultPayload[..maxLength] + "\n...(truncated)";
    }

    public async Task RegisterCallback(string callbackId, string grainType, string grainId, DateTimeOffset expiresAt, CancellationToken ct = default)
    {
        var value = $"{grainType}|{grainId}|{expiresAt:O}";
        State[$"{CallbackPrefix}{callbackId}"] = new StateEntry($"{CallbackPrefix}{callbackId}", value);
        await WriteStateAsync(ct);
    }

    public override async Task<AgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct = default)
    {
        var stateKey = $"{CallbackPrefix}{callbackId}";
        if (!State.TryGetValue(stateKey, out var entry))
            return new AgentResponse([]);

        var parts = entry.Value.ToString()!.Split('|', 3);
        if (parts.Length < 3)
            return new AgentResponse([]);

        var grainType = parts[0];
        var grainId = parts[1];
        var expiresAt = DateTimeOffset.Parse(parts[2]);

        if (DateTimeOffset.UtcNow > expiresAt)
        {
            State.Remove(stateKey);
            await WriteStateAsync(ct);
            return new AgentResponse([]);
        }

        var targetGrainId = Orleans.Runtime.GrainId.Create(grainType, grainId);
        var targetAgent = GrainFactory.GetGrain<IAgent>(targetGrainId);
        return await targetAgent.HandleCallback(callbackId, value, ct);
    }

    public Task<List<MediaPart>> GetPendingDeliveries(CancellationToken ct = default)
    {
        var deliveries = new List<MediaPart>(_pendingDeliveries);
        _pendingDeliveries.Clear();
        return Task.FromResult(deliveries);
    }

    public async Task<string?> GetTitle(CancellationToken ct)
    {
        if (State.TryGetValue("title", out var entry))
            return entry.Value.ToString();

        if (History.Count < 2)
            return null;

        var firstUser = History.FirstOrDefault(m => m.Role == "user")?.Text;
        var firstAssistant = History.FirstOrDefault(m => m.Role == "assistant")?.Text;
        if (firstUser is null) return null;

        var userSnippet = firstUser[..Math.Min(200, firstUser.Length)];
        var assistantSnippet = firstAssistant?[..Math.Min(200, firstAssistant.Length)] ?? "";
        var prompt = $"Generate a 2-5 word title for this conversation. Reply with ONLY the title, nothing else.\n\nUser: {userSnippet}\nAssistant: {assistantSnippet}";

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User, prompt)
        };
        var response = await ChatClient.GetResponseAsync(messages, cancellationToken: ct);
        var title = response.Text?.Trim().Trim('"') ?? "Chat";

        State["title"] = new StateEntry("title", title);
        await WriteStateAsync(ct);
        return title;
    }

    public async Task StartTaskDigestAsync(string taskId, TimeSpan interval, CancellationToken ct = default)
    {
        var jobName = $"{DigestJobPrefix}{taskId}";
        if (ScheduledJobs.ContainsKey(jobName))
            return;

        await ScheduleRecurringJob(jobName, interval, $"{DigestJobPrefix}{taskId}", ct);
    }

    public async Task StopTaskDigestAsync(string taskId, CancellationToken ct = default)
    {
        var jobName = $"{DigestJobPrefix}{taskId}";
        if (ScheduledJobs.ContainsKey(jobName))
            await CancelJob(jobName, ct);
    }

    private async Task ExecuteDigestAsync(string taskId, CancellationToken ct)
    {
        try
        {
            var ledger = GrainFactory.GetGrain<ITaskLedger>(taskId);
            var contextBlock = await ledger.GetContextBlockAsync(maxEvents: 10, ct);

            if (string.IsNullOrEmpty(contextBlock))
                return;

            var prompt = $"""
                Summarize this task progress in 1-2 sentences for the user.
                Be concise — this is a progress update, not a report.

                {contextBlock}
                """;

            var response = await ChatClient.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, prompt)],
                cancellationToken: ct);

            var summary = response.Text ?? "Task in progress...";

            logger.LogInformation("Digest for task {TaskId}: {Summary}", taskId, summary);

            await PublishAsync(IAWConstants.Events.OrchestrationProgress, new Dictionary<string, string>
            {
                [IAWConstants.PayloadKeys.TaskId] = taskId,
                [IAWConstants.PayloadKeys.Phase] = "digest",
                [IAWConstants.PayloadKeys.Message] = summary
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Digest failed for task {TaskId}", taskId);
        }
    }

}