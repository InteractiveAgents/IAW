namespace IAW.Core.Messages;

[GenerateSerializer]
public record AlertNotification(
    [property: Id(0)] string SourceAgentId,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string Severity,
    [property: Id(4)] string Message) : INotification;
