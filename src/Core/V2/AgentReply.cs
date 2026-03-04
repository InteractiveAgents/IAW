namespace Core.V2;

[GenerateSerializer]
public sealed class AgentReply
{
    [Id(0)]
    public string Output { get; set; } = string.Empty;

    [Id(1)]
    public string? ModelId { get; set; }

    [Id(2)]
    public Dictionary<string, string> Metadata { get; set; } = [];

    [Id(3)]
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}
