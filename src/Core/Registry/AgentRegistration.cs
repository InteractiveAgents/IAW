using IAW.Core;

namespace IAW.Core.Registry;

[GenerateSerializer]
public record AgentRegistration(
    [property: Id(0)] string AgentType,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string Description,
    [property: Id(3)] AgentKind Kind,
    [property: Id(4)] string[] Capabilities,
    [property: Id(5)] string[] Publishes,
    [property: Id(6)] string[] Subscribes);
