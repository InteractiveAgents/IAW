namespace Core.Contracts;

[GenerateSerializer]
public sealed record ScheduledJob
{
    [Id(0)] public string Id { get; init; } = string.Empty;
    [Id(1)] public string Name { get; init; } = string.Empty;
    [Id(2)] public string Description { get; init; } = string.Empty;
    [Id(3)] public TimeSpan Interval { get; init; }
    [Id(4)] public DateTimeOffset NextRunAt { get; init; }
    [Id(5)] public DateTimeOffset? LastRunAt { get; init; }
    [Id(6)] public string? LastResult { get; init; }
    [Id(7)] public bool Active { get; init; }
}