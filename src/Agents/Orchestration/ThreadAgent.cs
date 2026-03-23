using Core;
using Core.Context;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
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

    protected override int MaxHistoryMessages => 20;

    private IReadOnlyList<IAgentContextProvider>? _contextProviders;

    protected override IReadOnlyList<IAgentContextProvider> GetContextProviders()
    {
        if (_contextProviders is not null) return _contextProviders;

        var providers = new List<IAgentContextProvider>
        {
            new UserContextProvider(GrainFactory)
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

    protected override IReadOnlyList<AITool> DefineAdditionalTools()
    {
        return [
            AIFunctionFactory.Create(SendToAgentAsync, "SendToAgent",
                "Send a task to a specific agent by name. The agent handles it autonomously " +
                "with its own LLM and tools. Available agents: Shell, DotNet, FileSystem, Git, Roslyn, GitHub, Aspire."),

            AIFunctionFactory.Create(OrchestrateAsync, "Orchestrate",
                "For complex multi-step tasks requiring coordination across multiple agents. " +
                "NOT needed for single build/run/read/git tasks — use SendToAgent instead."),

            AIFunctionFactory.Create(SelfImproveAsync, "SelfImprove",
                "Fix a bug or improve the IAW system itself. Reads source code, analyzes the issue, " +
                "writes a fix, builds, tests, commits, and deploys via Aspire restart. " +
                "Use when the user reports a bug in the agent system or asks to improve/fix behavior.")
        ];
    }

    private async Task<string> SendToAgentAsync(string agentName, string request, CancellationToken ct = default)
    {
        logger.LogInformation("SendToAgent: {Agent} for: {Request}",
            agentName, request[..Math.Min(80, request.Length)]);

        var interfaceType = AgentInterfaceResolver.ResolveByDisplayName(agentName)
                         ?? AgentInterfaceResolver.Resolve(agentName);
        if (interfaceType is null)
            return $"Unknown agent: {agentName}. Available: Shell, DotNet, FileSystem, Git, Roslyn, GitHub.";

        var threadId = this.GetPrimaryKeyString();
        var agent = (IAgent)GrainFactory.GetGrain(interfaceType, $"{threadId}/{interfaceType.Name}");

        try
        {
            var result = await agent.GetResponse(request, ct);
            return result.Length > 4000
                ? result[..4000] + "\n...(truncated)"
                : result;
        }
        catch (OperationCanceledException)
        {
            return $"Agent {agentName} timed out. Try a simpler request or a different agent.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendToAgent: {Agent} failed", agentName);
            var suggestion = agentName switch
            {
                "DotNet" => "Try Shell agent for raw dotnet CLI commands, or check the project path.",
                "Shell" => "Check command syntax. For .NET operations, use DotNet agent instead.",
                "FileSystem" => "Check file path exists. Use absolute paths.",
                "Git" => "Check repository path. Ensure it's a valid git repo.",
                "Aspire" => "Aspire MCP may not be connected. Try again after restart.",
                "Roslyn" => "Check that the workspace is set and contains C# code.",
                _ => "Try a different agent or rephrase the request."
            };
            return $"Agent {agentName} failed: {ex.Message}\nSuggestion: {suggestion}";
        }
    }

    private async Task<string> SelfImproveAsync(string task, CancellationToken ct = default)
    {
        logger.LogInformation("SelfImprove: {Task}", task[..Math.Min(80, task.Length)]);

        var prompt = $"""
            SELF-IMPROVEMENT TASK: {task}

            You must accomplish this by calling SendToAgent multiple times. The IAW source code is at E:\IAW.
            Available agents: FileSystem (read/write files), DotNet (build/test), Git (commit), Aspire (restart to deploy), Roslyn (analyze code).

            RULES:
            - Use FileSystem to create or modify source files under E:\IAW\src\
            - Use DotNet to build E:\IAW\IAW.slnx after writing code
            - If build fails, use FileSystem to fix the code and rebuild
            - Use Git to commit changes after a successful build
            - Use Aspire to restart the assistant resource after successful build
            - Do NOT read traces unless debugging a specific runtime error
            - Write complete, compilable C# code — follow existing patterns in the codebase
            - For new agents: create an interface (IXxx.cs) and implementation (XxxAgent.cs) in src/Agents/

            Execute now. Use SendToAgent for each step.
            """;

        return await GetResponse(prompt, ct);
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

}