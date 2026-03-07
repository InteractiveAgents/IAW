using IAW.Core.Messages;

namespace IAW.Agents.Messages;

[GenerateSerializer]
public record ReviewFeedbackMessage(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Feedback,
    [property: Id(2)] string[] FilesAffected,
    [property: Id(3)] string SourceAgentId,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp) : IAgentMessage;
