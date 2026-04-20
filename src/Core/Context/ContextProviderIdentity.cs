using Microsoft.Agents.AI;

namespace Core.Context;

internal static class ContextProviderIdentity
{
    public const string UserIdKey = "iaw.userId";
    public const string ThreadIdKey = "iaw.threadId";

    public static string? ReadUserId()
    {
        var bag = AIAgent.CurrentRunContext?.Session?.StateBag;
        if (bag is null) return null;
        return bag.TryGetValue<string>(UserIdKey, out var id) ? id : null;
    }

    public static string? ReadThreadId()
    {
        var bag = AIAgent.CurrentRunContext?.Session?.StateBag;
        if (bag is null) return null;
        return bag.TryGetValue<string>(ThreadIdKey, out var id) ? id : null;
    }
}
