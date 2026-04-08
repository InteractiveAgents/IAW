using Core.Registry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public class AgentRoutingContextProvider(
    IGrainFactory grainFactory,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<AgentRoutingContextProvider>? logger = null) : IAgentContextProvider
{
    static readonly HashSet<string> OrchestrationAgents = ["IThread", "IAgentSelector", "ICodeOrchestrator", "ITelegramUI"];

    public string Name => "agent-routing";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        try
        {
            var registry = grainFactory.GetGrain<IAgentRegistry>("global");

            ReadOnlyMemory<float> queryVector = default;
            try
            {
                var embeddings = await embeddingGenerator.GenerateAsync([prompt], cancellationToken: ct);
                queryVector = embeddings[0].Vector;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Embedding generation failed, falling back to keyword search");
            }

            var candidates = queryVector.Length > 0
                ? await registry.HybridSearchAsync(prompt, queryVector, top: 8, ct: ct)
                : await registry.SearchAsync(prompt, top: 8, ct: ct);

            var filtered = candidates
                .Where(c => !OrchestrationAgents.Contains(c.InterfaceName))
                .Take(5)
                .ToList();

            // if search returned nothing, show all available agents so LLM can choose
            if (filtered.Count == 0)
            {
                var allAgents = await registry.GetAllAsync(ct);
                filtered = allAgents
                    .Where(r => !OrchestrationAgents.Contains(r.InterfaceName) && r.DisplayName.Length > 0)
                    .Select(r => new AgentCandidate(r.AgentType, r.Namespace, r.DisplayName, r.Description, r.InterfaceName, 0f))
                    .ToList();
            }

            if (filtered.Count == 0)
                return [];

            var lines = new List<string>(filtered.Count + 1)
            {
                "[Available agents for this request]"
            };

            foreach (var c in filtered)
                lines.Add($"- {c.DisplayName}: {c.Description}");

            return lines;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Agent routing context failed");
            return [];
        }
    }
}
