namespace Core.V3;

[GenerateSerializer]
public record TrackingItem(
    [property: Id(0)] string Id,
    [property: Id(1)] string Description,
    [property: Id(2)] TimeSpan Interval,
    [property: Id(3)] DateTimeOffset CreatedAt,
    [property: Id(4)] DateTimeOffset? LastCheckAt,
    [property: Id(5)] string? LastResult);
