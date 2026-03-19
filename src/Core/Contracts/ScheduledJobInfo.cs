namespace Core.Contracts;

[GenerateSerializer]
public record ScheduledJobInfo(
    [property: Id(0)] string Name,
    [property: Id(1)] string Prompt,
    [property: Id(2)] TimeSpan Interval,
    [property: Id(3)] DateTimeOffset? NextDue);
