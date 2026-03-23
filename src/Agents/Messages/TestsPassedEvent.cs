using Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record TestsPassedEvent(
    [property: Id(0)] string[] TestFiles,
    [property: Id(1)] int Passed,
    [property: Id(2)] int Failed,
    [property: Id(3)] string SourceAgentId,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp) : IEvent;