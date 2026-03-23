namespace Core.Contracts.UI;

[GenerateSerializer]
public abstract record WidgetState
{
    [Id(0)] public string Id { get; init; } = string.Empty;
    [Id(1)] public string ProjectSlug { get; init; } = string.Empty;
    [Id(2)] public int MessageId { get; init; }
    [Id(3)] public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public sealed record ButtonGridState : WidgetState
{
    [Id(10)] public IReadOnlyList<ButtonRow> Rows { get; init; } = Array.Empty<ButtonRow>();
    [Id(11)] public string? SelectedValue { get; init; }
}

[GenerateSerializer]
public sealed record PaginatorState : WidgetState
{
    [Id(10)] public IReadOnlyList<string> Items { get; init; } = Array.Empty<string>();
    [Id(11)] public int PageSize { get; init; }
    [Id(12)] public int CurrentPage { get; init; }
}

[GenerateSerializer]
public sealed record WizardState : WidgetState
{
    [Id(10)] public IReadOnlyList<WizardStep> Steps { get; init; } = Array.Empty<WizardStep>();
    [Id(11)] public int CurrentStep { get; init; }
    [Id(12)] public IReadOnlyDictionary<string, string> Collected { get; init; } = new Dictionary<string, string>();
}

[GenerateSerializer]
public sealed record MenuState : WidgetState
{
    [Id(10)] public MenuNode Root { get; init; } = new("Root", null, Array.Empty<MenuNode>());
    [Id(11)] public IReadOnlyList<string> BreadCrumb { get; init; } = Array.Empty<string>();
}

[GenerateSerializer]
public sealed record FormState : WidgetState
{
    [Id(10)] public IReadOnlyList<FormField> Fields { get; init; } = Array.Empty<FormField>();
    [Id(11)] public int CurrentField { get; init; }
    [Id(12)] public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
}