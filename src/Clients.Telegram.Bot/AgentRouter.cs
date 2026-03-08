using Microsoft.Extensions.AI;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace TelegramBot;

public sealed class AgentRouter(
    QdrantClient qdrant,
    [FromKeyedServices("embedding")] IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<AgentRouter> logger) : Grain, IAgentRouter
{
    private const string CollectionName = "agent-routing";
    private const float ConfidenceThreshold = 0.7f;
    private const string PersonalAssistantId = "personal-assistant";

    private static readonly (string Id, string Description)[] AgentDescriptions =
    [
        ("personal-assistant", "General assistant, task decomposition, team coordination, complex multi-step requests"),
        ("knowledge", "Project knowledge, architecture decisions, patterns, conventions, tech stack"),
        ("user", "User preferences, settings, memories, personal configuration"),
        ("fs", "File operations, reading files, writing files, searching code, listing directories"),
        ("shell", "Shell commands, terminal operations, system administration"),
        ("git", "Git version control, commits, diffs, logs, branches, reverts"),
        ("build", "Building .NET projects, compiling code, running tests"),
        ("aspire", "Aspire resources, service orchestration, health monitoring, resource management"),
        ("roslyn", "C# code analysis, type maps, architecture analysis, pattern detection, Roslyn"),
        ("dotnet", "dotnet CLI, testing, code formatting, .NET development"),
        ("nuget", "NuGet packages, dependency management, outdated packages"),
        ("github", "GitHub operations, pull requests, issues, releases, repository management"),
        ("reviewer", "Code review, quality analysis, best practices"),
        ("self-improvement", "Code quality analysis, improvement proposals, self-modification"),
        ("planning", "Execution plans, task planning, agent coordination"),
        ("notification", "Alerts, notifications, event aggregation"),
        ("deployer", "Deployment, release builds, Aspire deployment")
    ];

    private bool _registryBuilt;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        if (!_registryBuilt)
        {
            await RebuildRegistryAsync(cancellationToken);
            _registryBuilt = true;
        }
    }

    public async Task<AgentRouteResult> RouteAsync(string message, CancellationToken ct = default)
    {
        try
        {
            var messageVector = await EmbedSingleAsync(message, ct);
            var searchResult = await qdrant.SearchAsync(
                CollectionName,
                messageVector,
                limit: 1,
                cancellationToken: ct);

            if (searchResult.Count > 0 && searchResult[0].Score >= ConfidenceThreshold)
            {
                var agentId = searchResult[0].Payload["agentId"].StringValue;
                return new AgentRouteResult
                {
                    AgentId = agentId,
                    Confidence = searchResult[0].Score,
                    Escalated = false
                };
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Qdrant routing failed, escalating to PersonalAssistant");
        }

        return new AgentRouteResult
        {
            AgentId = PersonalAssistantId,
            Confidence = 0,
            Escalated = true
        };
    }

    public async Task RebuildRegistryAsync(CancellationToken ct = default)
    {
        try
        {
            var collections = await qdrant.ListCollectionsAsync(ct);
            if (!collections.Any(c => c == CollectionName))
            {
                var sampleVector = await EmbedSingleAsync("test", ct);
                await qdrant.CreateCollectionAsync(CollectionName,
                    new VectorParams { Size = (ulong)sampleVector.Length, Distance = Distance.Cosine },
                    cancellationToken: ct);
            }

            var descriptions = AgentDescriptions.Select(a => a.Description).ToList();
            var allVectors = await embeddings.GenerateAsync(descriptions, cancellationToken: ct);

            var points = new List<PointStruct>();
            for (var i = 0; i < AgentDescriptions.Length; i++)
            {
                points.Add(new PointStruct
                {
                    Id = (ulong)(i + 1),
                    Vectors = allVectors[i].Vector.ToArray(),
                    Payload = { ["agentId"] = AgentDescriptions[i].Id }
                });
            }

            await qdrant.UpsertAsync(CollectionName, points, cancellationToken: ct);
            logger.LogInformation("Agent routing registry rebuilt with {Count} agents", points.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rebuild agent routing registry");
        }
    }

    private async Task<float[]> EmbedSingleAsync(string text, CancellationToken ct)
    {
        var result = await embeddings.GenerateAsync([text], cancellationToken: ct);
        return result[0].Vector.ToArray();
    }
}
