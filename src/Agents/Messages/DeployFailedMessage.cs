using Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record DeployFailedMessage(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Error,
    [property: Id(2)] string SourceAgentId,
    [property: Id(3)] string CorrelationId,
    [property: Id(4)] DateTimeOffset Timestamp) : IAgentMessage;
