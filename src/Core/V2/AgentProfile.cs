namespace Core.V2;

[GenerateSerializer]
public sealed class AgentProfile
{
    [Id(0)]
    public string Id { get; set; } = string.Empty;

    [Id(1)]
    public string DisplayName { get; set; } = string.Empty;

    [Id(2)]
    public string? Description { get; set; }

    [Id(3)]
    public string Instructions { get; set; } = string.Empty;

    [Id(4)]
    public List<string> Capabilities { get; set; } = [];
}
