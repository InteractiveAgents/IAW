namespace Core.Contracts;

[GenerateSerializer]
public record AgentMetadata(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] string[] Publishes,
    [property: Id(4)] string[] Subscribes);

[GenerateSerializer]
public record ToolDescription(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description);