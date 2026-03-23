namespace Core.Contracts;

[GenerateSerializer]
public sealed record ChatMessage
{
    [Id(0)] public string Role { get; init; } = string.Empty;
    [Id(1)] public string Content { get; init; } = string.Empty;
    [Id(2)] public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    [Id(3)] public List<ContentPart> Parts { get; init; } = [];

    public string Text => Parts.Count > 0
        ? string.Join("", Parts.OfType<TextContent>().Select(p => p.Text))
        : Content ?? string.Empty;
}