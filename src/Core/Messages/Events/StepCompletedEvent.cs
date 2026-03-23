namespace Core.Messages.Events;

[GenerateSerializer]
public record StepCompletedEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string TaskId,
    [property: Id(4)] int StepIndex,
    [property: Id(5)] string Output,
    [property: Id(6)] TimeSpan Duration) : ITaskStreamEvent;