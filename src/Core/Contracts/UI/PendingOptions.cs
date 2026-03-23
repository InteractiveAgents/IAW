namespace Core.Contracts.UI;

[GenerateSerializer]
public sealed record PendingOptions(
    [property: Id(0)] string CallbackId,
    [property: Id(1)] string Prompt,
    [property: Id(2)] IReadOnlyList<PendingOption> Options,
    [property: Id(3)] DateTimeOffset ExpiresAt);

[GenerateSerializer]
public sealed record PendingOption(
    [property: Id(0)] string Label,
    [property: Id(1)] string Value);

[GenerateSerializer]
public sealed record PendingOptionSet(
    [property: Id(0)] string Id,
    [property: Id(1)] string Prompt,
    [property: Id(2)] IReadOnlyList<PendingOption> Options,
    [property: Id(3)] string ProjectSlug,
    [property: Id(4)] DateTimeOffset CreatedAt,
    [property: Id(5)] string Type = "option");
