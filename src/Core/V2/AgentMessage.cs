namespace Core.V2;

[GenerateSerializer]
public sealed class AgentMessage
{
    [Id(0)]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    [Id(1)]
    public string Role { get; set; } = string.Empty;

    [Id(2)]
    public string Content { get; set; } = string.Empty;

    [Id(3)]
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    [Id(4)]
    public Dictionary<string, string> Metadata { get; set; } = [];
}
