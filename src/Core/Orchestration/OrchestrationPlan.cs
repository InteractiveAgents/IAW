namespace Core.Orchestration;

[GenerateSerializer]
public record OrchestrationPlan(
    [property: Id(0)] string Summary,
    [property: Id(1)] IReadOnlyList<PlanStep> Steps);

[GenerateSerializer]
public record PlanStep(
    [property: Id(0)] int Order,
    [property: Id(1)] string AgentType,
    [property: Id(2)] string Action,
    [property: Id(3)] Dictionary<string, string> Parameters);
