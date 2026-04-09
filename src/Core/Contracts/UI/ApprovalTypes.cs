namespace Core.Contracts.UI;

[GenerateSerializer]
public sealed record CallbackResult(
    [property: Id(0)] string? NewText,
    [property: Id(1)] string? Action,
    [property: Id(2)] string? Toast,
    [property: Id(3)] IReadOnlyList<Button>? Buttons = null);
