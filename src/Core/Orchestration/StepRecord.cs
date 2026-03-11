namespace Core.Orchestration;

[GenerateSerializer]
public record StepRecord(
    [property: Id(0)] int Index,
    [property: Id(1)] string AgentId,
    [property: Id(2)] string Action,
    [property: Id(3)] StepStatus Status,
    [property: Id(4)] Dictionary<string, string> Parameters);
