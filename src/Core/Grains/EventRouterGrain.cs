using Core.Contracts;
using Core.Contracts.Events;

namespace Core.Grains;

[GrainType(IAWConstants.GrainTypes.EventRouter)]
public class EventRouterGrain : Grain, IEventRouter
{
    private static readonly List<RoutingRule> Rules =
    [
        new(AgentEventType.BuildFailed, "filesystem", "fix", "CS0246"),
        new(AgentEventType.BuildFailed, "roslyn", "analyze"),
        new(AgentEventType.TestFailed, "dotnet", "diagnose"),
        new(AgentEventType.ValidationFailed, "code-orchestrator", "retry"),
        new(AgentEventType.HealthCritical, "thread", "escalate"),
        new(AgentEventType.HealthWarning, "aspire", "investigate"),
        new(AgentEventType.DeployFailed, "thread", "escalate"),
    ];

    public Task<RoutingResult?> RouteAsync(TaskEvent evt, CancellationToken ct = default)
    {
        foreach (var rule in Rules)
        {
            if (rule.EventAction != evt.Action)
                continue;

            if (rule.ErrorCodePattern is not null
                && evt.Result.Contains(rule.ErrorCodePattern, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<RoutingResult?>(
                    new RoutingResult(rule.TargetAgentType, rule.Action, evt.Result));
            }

            if (rule.ErrorCodePattern is null)
            {
                return Task.FromResult<RoutingResult?>(
                    new RoutingResult(rule.TargetAgentType, rule.Action, evt.Result));
            }
        }

        return Task.FromResult<RoutingResult?>(null);
    }

    public Task<IReadOnlyList<RoutingRule>> GetRulesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RoutingRule>>(Rules);
}
