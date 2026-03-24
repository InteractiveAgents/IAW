namespace Core.Contracts.UI;

[GenerateSerializer]
public sealed record ButtonRow([property: Id(0)] IReadOnlyList<Button> Buttons);

[GenerateSerializer]
public sealed record Button(
    [property: Id(0)] string Text,
    [property: Id(1)] string CallbackData,
    [property: Id(2)] string? Url);

[GenerateSerializer]
public sealed record WizardStep(
    [property: Id(0)] string Id,
    [property: Id(1)] string Prompt,
    [property: Id(2)] IReadOnlyList<Button> Options);

[GenerateSerializer]
public sealed record MenuNode(
    [property: Id(0)] string Label,
    [property: Id(1)] string? Action,
    [property: Id(2)] IReadOnlyList<MenuNode>? Children);

[GenerateSerializer]
public sealed record FormField(
    [property: Id(0)] string Name,
    [property: Id(1)] string Prompt,
    [property: Id(2)] FormFieldType Type,
    [property: Id(3)] IReadOnlyList<Button>? Options);

[GenerateSerializer]
public enum FormFieldType { SingleChoice, MultiChoice, FreeText }