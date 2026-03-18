using Core.Contracts;

namespace Core.Registry;

[GenerateSerializer]
public record AgentRegistration(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] string[] Publishes,
    [property: Id(4)] string[] Subscribes);
