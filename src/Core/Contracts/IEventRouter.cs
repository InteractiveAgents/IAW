namespace Core.Contracts;

public interface IEventRouter : IGrainWithStringKey
{
    Task<RoutingResult?> RouteAsync(TaskEvent evt, CancellationToken ct = default);
    Task<IReadOnlyList<RoutingRule>> GetRulesAsync(CancellationToken ct = default);
}
