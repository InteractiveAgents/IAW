namespace Core.Contracts;

public interface IEventRouter : IGrainWithStringKey
{
    Task<RoutingResult?> RouteAsync(TaskEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<RoutingRule>> GetRulesAsync(CancellationToken ct = default);
    Task AddRuleAsync(RoutingRule rule, CancellationToken ct = default);
    Task RemoveRuleAsync(string eventAction, string? errorCodePattern = null, CancellationToken ct = default);
}
