namespace Core.Ingestion;

public interface IIngestionSource
{
    Task<IReadOnlyList<IngestedChunk>> ExtractChunksAsync(Stream source, string fileName, CancellationToken ct = default);
}