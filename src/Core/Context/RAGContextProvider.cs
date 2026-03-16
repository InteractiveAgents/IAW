using Microsoft.Extensions.AI;
using Qdrant.Client;

namespace Core.Context;

public class RAGContextProvider(
    QdrantClient qdrantClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator) : IAgentContextProvider
{
    public string Name => "document-search";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        var collectionName = $"project-{agentId.Replace("/", "-")}";

        try
        {
            if (!await qdrantClient.CollectionExistsAsync(collectionName, ct))
                return [];

            var embeddings = await embeddingGenerator.GenerateAsync([prompt], cancellationToken: ct);
            var queryVector = embeddings[0].Vector.ToArray();
            var results = await qdrantClient.SearchAsync(
                collectionName, queryVector, limit: 5, cancellationToken: ct);

            return [.. results
                .Select(r =>
                    $"[document: {r.Payload["fileName"]}, page {r.Payload["pageNumber"]}] {r.Payload["text"]}")];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }
}
