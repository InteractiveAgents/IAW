namespace IAW.Core.Registry;

[GenerateSerializer]
public record AgentQuery(
    [property: Id(0)] AgentKind? Kind = null,
    [property: Id(1)] string[]? Publishes = null,
    [property: Id(2)] string[]? Subscribes = null);
