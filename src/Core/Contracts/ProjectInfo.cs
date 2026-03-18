namespace Core.Contracts;

[GenerateSerializer]
public sealed record ProjectInfo([property: Id(0)] string Slug, [property: Id(1)] string TopicId);
