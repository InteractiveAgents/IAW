namespace Core.Messages.Events;

[GenerateSerializer]
public record ConsiliumResponseEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string TaskId,
    [property: Id(4)] string ModelId,
    [property: Id(5)] string Response,
    [property: Id(6)] double Confidence) : ITaskStreamEvent;
