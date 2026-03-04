namespace Core.V2;

[GenerateSerializer]
public sealed class AgentMessageQuery
{
    [Id(0)]
    public int? Limit { get; set; }

    [Id(1)]
    public DateTimeOffset? SinceUtc { get; set; }

    [Id(2)]
    public string? Role { get; set; }

    [Id(3)]
    public bool Descending { get; set; }
}
