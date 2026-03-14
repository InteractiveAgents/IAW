namespace Core.Contracts.UI;

[GenerateSerializer]
public abstract record WidgetState
{
    [Id(0)] public string Id { get; init; } = string.Empty;
    [Id(1)] public string ProjectSlug { get; init; } = string.Empty;
    [Id(2)] public int MessageId { get; init; }
}

[GenerateSerializer]
public sealed record ButtonGridState : WidgetState
{
    [Id(10)] public IReadOnlyList<ButtonRow> Rows { get; init; } = [];
    [Id(11)] public string? SelectedValue { get; init; }
}

[GenerateSerializer]
public sealed record PaginatorState : WidgetState
{
    [Id(10)] public IReadOnlyList<string> Items { get; init; } = [];
    [Id(11)] public int PageSize { get; init; }
    [Id(12)] public int CurrentPage { get; init; }
}

[GenerateSerializer]
public sealed record WizardState : WidgetState
{
    [Id(10)] public IReadOnlyList<WizardStep> Steps { get; init; } = [];
    [Id(11)] public int CurrentStep { get; init; }
    [Id(12)] public IReadOnlyDictionary<string, string> Collected { get; init; } = new Dictionary<string, string>();
}

[GenerateSerializer]
public sealed record MenuState : WidgetState
{
    [Id(10)] public MenuNode Root { get; init; } = new("Root", null, []);
    [Id(11)] public IReadOnlyList<string> BreadCrumb { get; init; } = [];
}

[GenerateSerializer]
public sealed record FormState : WidgetState
{
    [Id(10)] public IReadOnlyList<FormField> Fields { get; init; } = [];
    [Id(11)] public int CurrentField { get; init; }
    [Id(12)] public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
}
