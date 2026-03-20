using Core;
using Core.Context;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Qdrant.Client;
using AgentResponse = global::Core.UI.AgentResponse;

namespace IAW.Agents.Orchestration;

[GrainType(IAWConstants.GrainTypes.Thread)]
public class ThreadAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
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
