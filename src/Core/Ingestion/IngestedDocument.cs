namespace Core.Ingestion;

[GenerateSerializer]
public sealed record IngestedDocument(
    [property: Id(0)] string FileName,
    [property: Id(1)] string BlobUri,
    [property: Id(2)] IReadOnlyList<IngestedChunk> Chunks,
    [property: Id(3)] DateTimeOffset IngestedAt);
