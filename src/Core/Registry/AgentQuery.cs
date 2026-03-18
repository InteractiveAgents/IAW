namespace Core.Registry;

[GenerateSerializer]
public record AgentQuery(
    [property: Id(0)] string[]? Publishes = null,
    [property: Id(1)] string[]? Subscribes = null);
