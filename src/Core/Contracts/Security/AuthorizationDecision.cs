namespace Core.Contracts.Security;

[GenerateSerializer]
public enum AuthorizationOutcome
{
    Allow = 0,
    Deny = 1
}

[GenerateSerializer]
public sealed record AuthorizationDecision(
    [property: Id(0)] AuthorizationOutcome Outcome,
    [property: Id(1)] string Reason,
    [property: Id(2)] AuthorizationScope AppliedScope = AuthorizationScope.Once);
