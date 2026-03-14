namespace Core.Contracts;

[GenerateSerializer]
public sealed record ScheduledJob(
    [property: Id(0)] string Id,
    [property: Id(1)] string Name);
