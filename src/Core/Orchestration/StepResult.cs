namespace Core.Orchestration;

[GenerateSerializer]
public record StepResult(
    [property: Id(0)] string Output,
    [property: Id(1)] TimeSpan Duration,
    [property: Id(2)] string AgentId,
    [property: Id(3)] DateTimeOffset CompletedAt);
