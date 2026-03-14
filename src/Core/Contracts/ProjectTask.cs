namespace Core.Contracts;

[GenerateSerializer]
public sealed record ProjectTask
{
    [Id(0)] public string Id { get; init; } = string.Empty;
    [Id(1)] public string Description { get; init; } = string.Empty;
    [Id(2)] public TaskPriority Priority { get; init; }
    [Id(3)] public ProjectTaskStatus Status { get; init; }
    [Id(4)] public string? AssignedAgent { get; init; }
    [Id(5)] public DateTimeOffset CreatedAt { get; init; }
    [Id(6)] public DateTimeOffset? CompletedAt { get; init; }
    [Id(7)] public string? Result { get; init; }
}
