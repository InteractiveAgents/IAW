namespace IAW.Core.Messages;

public interface IAgentMessage
{
    string SourceAgentId { get; }
    string CorrelationId { get; }
    DateTimeOffset Timestamp { get; }
}
