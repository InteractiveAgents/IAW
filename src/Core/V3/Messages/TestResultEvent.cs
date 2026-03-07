namespace Core.V3.Messages;

[GenerateSerializer]
public record TestResultEvent(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] bool Passed,
    [property: Id(4)] int TotalTests,
    [property: Id(5)] int FailedTests,
    [property: Id(6)] string? Summary = null) : IEvent;
