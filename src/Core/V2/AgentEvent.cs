namespace Core.V2;

[GenerateSerializer]
public sealed class AgentEvent
{
    [Id(0)]
    public string EventId { get; set; } = Guid.NewGuid().ToString("N");

    [Id(1)]
    public string Type { get; set; } = string.Empty;

    [Id(2)]
    public string? Payload { get; set; }

    [Id(3)]
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    [Id(4)]
    public Dictionary<string, string> Metadata { get; set; } = [];
}
