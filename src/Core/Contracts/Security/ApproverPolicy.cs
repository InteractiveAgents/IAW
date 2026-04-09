namespace Core.Contracts.Security;

[GenerateSerializer]
public sealed record ApproverPolicy(
    [property: Id(0)] string Id,
    [property: Id(1)] AuthorizationScope Scope,
    [property: Id(2)] string? ThreadId,
    [property: Id(3)] string Rule,
    [property: Id(4)] DateTimeOffset CreatedAt);
