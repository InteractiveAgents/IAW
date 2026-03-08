using Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record BuildMetricsCollectedEvent(
    [property: Id(0)] int TotalBuilds,
    [property: Id(1)] int FailedBuilds,
    [property: Id(2)] double AverageBuildTimeMs,
    [property: Id(3)] int TotalWarnings,
    [property: Id(4)] int TotalTestsPassed,
    [property: Id(5)] int TotalTestsFailed,
    [property: Id(6)] string SourceAgentId,
    [property: Id(7)] string CorrelationId,
    [property: Id(8)] DateTimeOffset Timestamp) : IEvent;
