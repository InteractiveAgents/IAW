namespace Core.Ingestion;

[GenerateSerializer]
public sealed record IngestedChunk(
    [property: Id(0)] string Text,
    [property: Id(1)] int PageNumber,
    [property: Id(2)] string FileName);