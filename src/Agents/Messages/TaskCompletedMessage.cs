using IAW.Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record TaskCompletedMessage(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Result,
    [property: Id(2)] string CompletedBy,
    [property: Id(3)] string SourceAgentId,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp) : IAgentMessage;
