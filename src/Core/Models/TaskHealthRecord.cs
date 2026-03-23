namespace Core.Models;

[GenerateSerializer]
public record TaskHealthRecord(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string OrchestratorId,
    [property: Id(2)] DateTimeOffset StartedAt,
    [property: Id(3)] DateTimeOffset LastProgressAt,
    [property: Id(4)] int StepCount,
    [property: Id(5)] int CompletedSteps,
    [property: Id(6)] bool IsStalled,
    [property: Id(7)] string? StallReason);