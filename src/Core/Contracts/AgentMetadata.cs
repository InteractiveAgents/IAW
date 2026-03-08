namespace IAW.Core;

[GenerateSerializer]
public record AgentMetadata(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] AgentKind Kind,
    [property: Id(4)] string[] Publishes,
    [property: Id(5)] string[] Subscribes);

[GenerateSerializer]
public enum AgentKind { Static, Dynamic }

[GenerateSerializer]
public record ToolDescription(
    [property: Id(0)] string Name,
    [property: Id(1)] string Description);
