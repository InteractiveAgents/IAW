namespace Core.Contracts;

[GenerateSerializer]
public record ScheduledJobItem(
    [property: Id(0)] string Name,
    [property: Id(1)] string Prompt,
    [property: Id(2)] TimeSpan Interval,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] bool IsRecurring,
    [property: Id(5)] DateTimeOffset? LastRunAt,
    [property: Id(6)] string? LastResult,
    [property: Id(7)] string? DurableJobId = null,
    [property: Id(8)] string? DurableJobShardId = null);
