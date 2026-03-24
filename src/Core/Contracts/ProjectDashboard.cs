namespace Core.Contracts;

[GenerateSerializer]
public sealed record ProjectDashboard
{
    [Id(0)] public IReadOnlyList<ProjectTask> Tasks { get; init; } = [];
    [Id(1)] public IReadOnlyList<ScheduledJob> Jobs { get; init; } = [];
    [Id(2)] public IReadOnlyList<FileReference> Files { get; init; } = [];
    [Id(3)] public DateTimeOffset GeneratedAt { get; init; }
}