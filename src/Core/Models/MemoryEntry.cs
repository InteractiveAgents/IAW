namespace Core.Models;

[GenerateSerializer]
public record MemoryEntry(
    [property: Id(0)] string Id,
    [property: Id(1)] string Content,
    [property: Id(2)] MemoryProvenance Source,
    [property: Id(3)] float RelevanceScore,
    [property: Id(4)] DateTimeOffset CreatedAt,
    [property: Id(5)] DateTimeOffset LastAccessedAt,
    [property: Id(6)] int AccessCount,
    [property: Id(7)] string? SupersededBy)
{
    [Id(8)] public float[]? Embedding { get; init; }
}