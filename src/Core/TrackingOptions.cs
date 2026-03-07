namespace IAW.Core;

public sealed record TrackingOptions
{
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(1);
    public int? MaxTicks { get; init; }
    public TimeSpan? MaxRunDuration { get; init; }
}

public sealed record TrackingStatus(
    bool IsTracking,
    int TickCount,
    DateTimeOffset? StartedAt,
    TrackingOptions Options);
