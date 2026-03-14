namespace Core.Contracts;

[GenerateSerializer]
public sealed record ProjectTask(
    [property: Id(0)] string Id,
    [property: Id(1)] string Description,
    [property: Id(2)] TaskPriority Priority,
    [property: Id(3)] ProjectTaskStatus Status);
