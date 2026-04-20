using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Qdrant.Client;

namespace Core.Context;

public sealed class RAGContextProvider(
    QdrantClient qdrantClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    ILogger<RAGContextProvider>? logger = null)
    : MessageAIContextProvider
{
    protected override async ValueTask<IEnumerable<Microsoft.Extensions.AI.ChatMessage>> ProvideMessagesAsync(
        MessageAIContextProvider.InvokingContext context, CancellationToken cancellationToken = default)
    {
        var threadId = ContextProviderIdentity.ReadThreadId();
        var userId = ContextProviderIdentity.ReadUserId();
        var projectId = threadId ?? userId;
        if (projectId is null)
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

        var collectionName = $"project-{projectId.Replace("/", "-")}";
        var query = context.RequestMessages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

        try
        {
            if (!await qdrantClient.CollectionExistsAsync(collectionName, cancellationToken))
                return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

            var embeddings = await embeddingGenerator.GenerateAsync([query], cancellationToken: cancellationToken);
            var queryVector = embeddings[0].Vector.ToArray();
            var results = await qdrantClient.SearchAsync(
                collectionName, queryVector, limit: 5, cancellationToken: cancellationToken);
            if (results.Count == 0)
                return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();

            var lines = new List<string> { "## Relevant documents" };
            foreach (var r in results)
            {
                var fileName = r.Payload.TryGetValue("fileName", out var fn) ? fn.StringValue : "?";
                var page = r.Payload.TryGetValue("pageNumber", out var p) ? p.IntegerValue.ToString() : "?";
                var text = r.Payload.TryGetValue("text", out var t) ? t.StringValue : "";
                lines.Add($"[{fileName}, page {page}] {text}");
            }

            return new[] { new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, string.Join("\n", lines)) };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "RAG context provider failed for project {ProjectId}", projectId);
            return Array.Empty<Microsoft.Extensions.AI.ChatMessage>();
        }
    }
}
