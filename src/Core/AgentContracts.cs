using Orleans;

namespace Core;

[GenerateSerializer]
public sealed class AgentMetadata
{
    [Id(0)]
    public string Id { get; set; } = string.Empty;

    [Id(1)]
    public string DisplayName { get; set; } = string.Empty;

    [Id(2)]
    public List<string> Capabilities { get; set; } = [];
}

[GenerateSerializer]
public sealed class AgentHistoryEntry
{
    [Id(0)]
    public string Role { get; set; } = string.Empty;

    [Id(1)]
    public string Content { get; set; } = string.Empty;

    [Id(2)]
    public DateTimeOffset TimestampUtc { get; set; }
}

[GenerateSerializer]
public sealed class AgentEventRecord
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;

    [Id(1)]
    public string? Payload { get; set; }

    [Id(2)]
    public DateTimeOffset TimestampUtc { get; set; }
}

[GenerateSerializer]
public sealed class NotificationEnvelope
{
    [Id(0)]
    public string Topic { get; set; } = string.Empty;

    [Id(1)]
    public string Payload { get; set; } = string.Empty;

    [Id(2)]
    public string ContentType { get; set; } = "application/json";

    [Id(3)]
    public string? Schema { get; set; }

    [Id(4)]
    public string? SchemaVersion { get; set; }

    [Id(5)]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    [Id(6)]
    public string? CorrelationId { get; set; }

    [Id(7)]
    public Dictionary<string, string> Headers { get; set; } = [];

    [Id(8)]
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public sealed class NotificationRecord
{
    [Id(0)]
    public string Topic { get; set; } = string.Empty;

    [Id(1)]
    public string Payload { get; set; } = string.Empty;

    [Id(2)]
    public DateTimeOffset TimestampUtc { get; set; }

    [Id(3)]
    public string ContentType { get; set; } = "application/json";

    [Id(4)]
    public string? Schema { get; set; }

    [Id(5)]
    public string? SchemaVersion { get; set; }

    [Id(6)]
    public string MessageId { get; set; } = string.Empty;

    [Id(7)]
    public string? CorrelationId { get; set; }

    [Id(8)]
    public Dictionary<string, string> Headers { get; set; } = [];
}

[GenerateSerializer]
public sealed class AgentTrackingStatus
{
    [Id(0)]
    public bool IsTracking { get; set; }

    [Id(1)]
    public int TickCount { get; set; }

    [Id(2)]
    public DateTimeOffset? StartedAtUtc { get; set; }

    [Id(3)]
    public TimeSpan Interval { get; set; }

    [Id(4)]
    public int MaxTicks { get; set; }
}
