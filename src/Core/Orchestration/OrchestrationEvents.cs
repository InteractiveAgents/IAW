using Core.Messages;

namespace Core.Orchestration;

[GenerateSerializer]
public record OrchestrationProgressEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] int StepIndex,
    [property: Id(2)] string Message,
    [property: Id(3)] DateTimeOffset Timestamp) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
}

[GenerateSerializer]
public record OrchestrationErrorEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] int StepIndex,
    [property: Id(2)] string ErrorType,
    [property: Id(3)] string ErrorMessage,
    [property: Id(4)] DateTimeOffset Timestamp) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
}

[GenerateSerializer]
public record OrchestrationArtifactEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string BlobPath,
    [property: Id(2)] string FileName,
    [property: Id(3)] string MimeType) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
    public DateTimeOffset Timestamp => DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public record OrchestrationCompletedEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Summary,
    [property: Id(2)] IReadOnlyList<string> ArtifactPaths,
    [property: Id(3)] DateTimeOffset Timestamp) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
}
