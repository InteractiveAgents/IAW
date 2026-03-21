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
