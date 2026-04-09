using Core.Registry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public sealed class AgentRoutingContextProvider(
    IGrainFactory grainFactory,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<AgentRoutingContextProvider>? logger = null)
    : MessageAIContextProvider
{
    static readonly HashSet<string> OrchestrationAgents = ["IThread", "IAgentSelector", "ICodeOrchestrator", "ITelegramUI"];

    protected override async ValueTask<IEnumerable<Microsoft.Extensions.AI.ChatMessage>> ProvideMessagesAsync(
        MessageAIContextProvider.InvokingContext context, CancellationToken cancellationToken = default)
    {
        var query = context.RequestMessages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

        try
        {
            var registry = grainFactory.GetGrain<IAgentRegistry>("global");

            ReadOnlyMemory<float> queryVector = default;
            try
            {
                var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
                queryVector = embeddings[0].Vector;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Embedding generation failed, falling back to keyword search");
            }

            var candidates = queryVector.Length > 0
                ? await registry.HybridSearchAsync(query, queryVector, top: 8, ct: cancellationToken)
                : await registry.SearchAsync(query, top: 8, ct: cancellationToken);

            var filtered = candidates
                .Where(c => !OrchestrationAgents.Contains(c.InterfaceName))
                .Take(5)
                .ToList();

            if (filtered.Count == 0)
            {
                var allAgents = await registry.GetAllAsync(cancellationToken);
                filtered = allAgents
                    .Where(r => !OrchestrationAgents.Contains(r.InterfaceName) && r.DisplayName.Length > 0)
                    .Select(r => new AgentCandidate(r.AgentType, r.Namespace, r.DisplayName, r.Description, r.InterfaceName, 0f) { Capabilities = r.Capabilities, RoutingExamples = r.RoutingExamples })
                    .ToList();
            }

            if (filtered.Count == 0)
                return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

            var lines = new List<string> { "## Available agents for this request" };
            foreach (var c in filtered)
            {
                var line = $"- {c.DisplayName}: {c.Description}";
                if (c.Capabilities.Length > 0)
                    line += $" [{string.Join(", ", c.Capabilities)}]";
                if (c.RoutingExamples.Length > 0)
                    line += $" Examples: \"{string.Join("\", \"", c.RoutingExamples)}\"";
                lines.Add(line);
            }

            return new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, string.Join("\n", lines)) };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Agent routing context failed");
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();
        }
    }
}
