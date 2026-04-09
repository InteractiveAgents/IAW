using Core.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Core.Registry;

public class AgentRegistrationStartupTask(
    IGrainFactory grainFactory,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<AgentRegistrationStartupTask> logger) : IStartupTask
{
    public async Task Execute(CancellationToken ct)
    {
        var registry = grainFactory.GetGrain<IAgentRegistry>("global");
        var records = DiscoverAndBuildRecords().ToList();

        await GenerateEmbeddingsAsync(records, ct);

        foreach (var record in records)
            await registry.RegisterAsync(record, ct);

        logger.LogInformation("Registered {Count} agents with embeddings", records.Count);
    }

    async Task GenerateEmbeddingsAsync(List<AgentRecord> records, CancellationToken ct)
    {
        try
        {
            var texts = records.Select(BuildEmbeddingText).ToList();
            var embeddings = await embeddingGenerator.GenerateAsync(texts, cancellationToken: ct);

            for (var i = 0; i < records.Count; i++)
                records[i].DescriptionEmbedding = embeddings[i].Vector;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to generate agent embeddings — falling back to keyword-only search");
        }
    }

    static string BuildEmbeddingText(AgentRecord r)
    {
        var parts = new List<string> { $"{r.DisplayName}: {r.Description}" };
        if (r.Capabilities.Length > 0)
            parts.Add(string.Join(" ", r.Capabilities));
        if (r.RoutingExamples.Length > 0)
            parts.Add(string.Join(". ", r.RoutingExamples));
        return string.Join(" ", parts);
    }

    public static IEnumerable<AgentRecord> DiscoverAndBuildRecords() =>
        DiscoverAgentTypes().Select(BuildRecord).Where(r => r is not null)!;

    static IEnumerable<Type> DiscoverAgentTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsClass: true }
                && t.IsSubclassOf(typeof(IAW.Core.Agent)));

    static AgentRecord? BuildRecord(Type agentType)
    {
        var agentInterface = agentType.GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && !i.IsGenericType);

        if (agentInterface is null)
            return null;

        var meta = AgentInterfaceMetadata.ReadFrom(agentInterface);

        var agentNamespace = ExtractNamespace(agentType);
        var displayName = meta.DisplayName.Length > 0
            ? meta.DisplayName
            : StripAgentSuffix(agentType.Name);

        return new AgentRecord
        {
            Id = Guid.NewGuid(),
            AgentType = agentType.Name,
            Namespace = agentNamespace,
            DisplayName = displayName,
            Description = meta.Description,
            Capabilities = meta.Capabilities,
            RoutingExamples = meta.RoutingExamples,
            InterfaceName = agentInterface.Name
        };
    }

    static string ExtractNamespace(Type type)
    {
        var ns = type.Namespace ?? "unknown";
        var lastDot = ns.LastIndexOf('.');
        return lastDot >= 0 ? ns[(lastDot + 1)..].ToLowerInvariant() : ns.ToLowerInvariant();
    }

    static string StripAgentSuffix(string typeName)
        => typeName.EndsWith("Agent") ? typeName[..^5] : typeName;
}
