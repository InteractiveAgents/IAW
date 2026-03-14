using Core.Services;
using Microsoft.Extensions.AI;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Core.Ingestion;

public sealed class DocumentIngestor(
    BlobFileStorage blobStorage,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    QdrantClient qdrantClient)
{
    private const uint VectorSize = 1536;

    public async Task<IngestedDocument> IngestAsync(
        string blobUri, string fileName, string projectId, CancellationToken ct = default)
    {
        var collectionName = $"project-{projectId.Replace("/", "-")}";

        using var blobStream = await blobStorage.DownloadAsync(blobUri);

        var ingestionSource = ResolveSource(fileName);
        var chunks = await ingestionSource.ExtractChunksAsync(blobStream, fileName, ct);

        await EnsureCollectionAsync(collectionName, ct);

        if (chunks.Count > 0)
        {
            var texts = chunks.Select(c => c.Text).ToList();
            var embeddings = await embeddingGenerator.GenerateAsync(texts, cancellationToken: ct);

            var points = new List<PointStruct>();
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var vector = embeddings[i].Vector.ToArray();

                points.Add(new PointStruct
                {
                    Id = (PointId)Guid.NewGuid(),
                    Vectors = vector,
                    Payload =
                    {
                        ["text"] = chunk.Text,
                        ["fileName"] = chunk.FileName,
                        ["pageNumber"] = chunk.PageNumber
                    }
                });
            }

            await qdrantClient.UpsertAsync(collectionName, points, cancellationToken: ct);
        }

        return new IngestedDocument(fileName, blobUri, chunks, DateTimeOffset.UtcNow);
    }

    private async Task EnsureCollectionAsync(string collectionName, CancellationToken ct)
    {
        var exists = await qdrantClient.CollectionExistsAsync(collectionName, ct);
        if (!exists)
        {
            await qdrantClient.CreateCollectionAsync(
                collectionName,
                new VectorParams { Size = VectorSize, Distance = Distance.Cosine },
                cancellationToken: ct);
        }
    }

    private static IIngestionSource ResolveSource(string fileName)
    {
        var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
        return extension switch
        {
            ".pdf" => new PdfIngestionSource(),
            _ => throw new NotSupportedException($"File type '{extension}' is not supported for ingestion.")
        };
    }
}
