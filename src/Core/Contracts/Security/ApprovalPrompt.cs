namespace Core.Contracts.Security;

[GenerateSerializer]
public sealed record ApprovalOption(
    [property: Id(0)] string Key,
    [property: Id(1)] string Label);

[GenerateSerializer]
public sealed record ApprovalPrompt(
    [property: Id(0)] string ApprovalId,
    [property: Id(1)] string UserId,
    [property: Id(2)] string ThreadId,
    [property: Id(3)] string Question,
    [property: Id(4)] IReadOnlyList<ApprovalOption> Options,
    [property: Id(5)] DateTimeOffset CreatedAt);

public static class ApprovalDecisionKeys
{
    public const string AllowOnce = "allow_once";
    public const string AllowThread = "allow_thread";
    public const string AllowUser = "allow_user";
    public const string Deny = "deny";

    public static AuthorizationScope KeyToScope(string key) => key switch
    {
        AllowOnce => AuthorizationScope.Once,
        AllowThread => AuthorizationScope.Thread,
        AllowUser => AuthorizationScope.User,
        _ => AuthorizationScope.Once
    };

    public static bool IsAllowKey(string key) =>
        key is AllowOnce or AllowThread or AllowUser;
}
