namespace Core.UI;

[GenerateSerializer]
public sealed record RichOutput(
    [property: Id(0)] string FormattedText,
    [property: Id(1)] IReadOnlyList<UIPart> Parts);