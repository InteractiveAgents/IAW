using IAW.Core.Messages;

namespace IAW.Core.Communication.Messages;

[GenerateSerializer]
public record TestResultMessage(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] int Total,
    [property: Id(2)] int Passed,
    [property: Id(3)] int Failed) : IAgentMessage
{
    [Id(4)] public string SourceAgentId { get; init; } = string.Empty;
    [Id(5)] public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    [Id(6)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
