namespace Core;

public interface IMonitorSourceProvider : IGrainWithStringKey
{
    Task<MonitorPollResult> PollAsync(MonitorPollRequest request, CancellationToken ct = default);
}

[GenerateSerializer]
public sealed class MonitorPollRequest
{
    [Id(0)] public string Source { get; set; } = string.Empty;
    [Id(1)] public string RawQuery { get; set; } = string.Empty;
    [Id(2)] public string? Cursor { get; set; }
    [Id(3)] public int MaxItems { get; set; } = 5;
    [Id(4)] public bool EmitInitialItems { get; set; }
}

[GenerateSerializer]
public sealed class MonitorPollResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public string ProviderId { get; set; } = string.Empty;
    [Id(2)] public string Status { get; set; } = string.Empty;
    [Id(3)] public string? NextCursor { get; set; }
    [Id(4)] public List<MonitorFeedItem> NewItems { get; set; } = [];
    [Id(5)] public DateTimeOffset CheckedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public sealed class MonitorFeedItem
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Title { get; set; } = string.Empty;
    [Id(2)] public string Url { get; set; } = string.Empty;
    [Id(3)] public DateTimeOffset? PublishedAtUtc { get; set; }
    [Id(4)] public string Summary { get; set; } = string.Empty;
}
