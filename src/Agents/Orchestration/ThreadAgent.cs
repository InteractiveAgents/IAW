using Core;
using Core.Context;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qdrant.Client;
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
        return [AIFunctionFactory.Create(DelegateAsync, "Delegate",
            "Delegate a task to the IAW agent system. Use this for any request that requires " +
            "code execution, system operations, builds, git, file operations, or specialized agent skills. " +
            "Describe WHAT needs to be done.")];
    }

    private async Task<string> DelegateAsync(string request, CancellationToken ct = default)
    {
        var taskId = $"dlg-{Guid.NewGuid().ToString("N")[..8]}";
        logger.LogInformation("Delegate: scheduling job {TaskId} for: {Request}",
            taskId, request[..Math.Min(80, request.Length)]);

        await ScheduleJob(taskId, TimeSpan.Zero, $"{IAWConstants.DelegationPrefix}{request}", ct);
        return $"Task {taskId} submitted. I'm working on your request and will deliver results shortly.";
    }

    private async Task<string> ExecuteSelection(SelectionResult selection, string request, CancellationToken ct)
    {
        var threadId = this.GetPrimaryKeyString();

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
        var plan = selection.Plan ?? $"Execute: {request}\nAgents: {string.Join(", ", selection.SelectedAgents)}";
        return await orchestrator.ExecuteCodeOrchestration(plan, ct);
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
