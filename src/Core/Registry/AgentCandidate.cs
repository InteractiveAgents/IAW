namespace Core.Registry;

[GenerateSerializer]
public record AgentCandidate(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string Namespace,
    [property: Id(2)] string DisplayName,
    [property: Id(3)] string Description,
    [property: Id(4)] string InterfaceName,
    [property: Id(5)] float Score);