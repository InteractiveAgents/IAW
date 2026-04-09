namespace Core.Contracts.Security;

[GenerateSerializer]
public enum AuthorizationScope
{
    Once = 0,
    Thread = 1,
    User = 2
}
