namespace Core.Contracts.Events;

[GenerateSerializer]
public sealed record DashboardChangedEvent(
    [property: Id(0)] string ProjectKey,
    [property: Id(1)] string RenderedMarkdown,
    [property: Id(2)] DateTimeOffset Timestamp);