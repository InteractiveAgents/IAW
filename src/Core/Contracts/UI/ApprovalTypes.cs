namespace Core.Contracts.UI;

[GenerateSerializer]
public sealed record PendingApproval(
    [property: Id(0)] string Id,
    [property: Id(1)] string Question,
    [property: Id(2)] IReadOnlyList<string> Options,
    [property: Id(3)] string ProjectSlug,
    [property: Id(4)] int MessageId,
    [property: Id(5)] DateTimeOffset CreatedAt);

[GenerateSerializer]
public sealed record ApprovalResult(
    [property: Id(0)] string ApprovalId,
    [property: Id(1)] string Decision,
    [property: Id(2)] string ProjectSlug);

[GenerateSerializer]
public sealed record CallbackResult(
    [property: Id(0)] string? NewText,
    [property: Id(1)] string? Action,
    [property: Id(2)] string? Toast);
