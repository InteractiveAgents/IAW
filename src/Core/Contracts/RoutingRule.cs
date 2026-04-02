namespace Core.Contracts;

[GenerateSerializer]
public record RoutingRule(
    [property: Id(0)] string EventAction,
    [property: Id(1)] string TargetAgentType,
    [property: Id(2)] string Action,
    [property: Id(3)] string? ErrorCodePattern = null);

[GenerateSerializer]
public record RoutingResult(
    [property: Id(0)] string TargetAgentType,
    [property: Id(1)] string Action,
    [property: Id(2)] string? Context = null);
