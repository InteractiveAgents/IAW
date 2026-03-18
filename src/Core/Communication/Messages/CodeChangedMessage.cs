using Core.Messages;

namespace Core.Communication.Messages;

[GenerateSerializer]
public record CodeChangedMessage(
    [property: Id(0)] string ProjectPath,
    [property: Id(1)] string FilePath,
    [property: Id(2)] string Description) : IAgentMessage
{
    [Id(3)] public string SourceAgentId { get; init; } = string.Empty;
    [Id(4)] public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    [Id(5)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
