using Core.Contracts;
using Core.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Core.Grains;

[GrainType(IAWConstants.GrainTypes.EventRouter)]
public class EventRouterGrain(
    [FromKeyedServices("routing-rules")] IDurableList<RoutingRule> rules)
    : DurableGrain, IEventRouter
{
    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        if (rules.Count == 0)
        {
            rules.Add(new(AgentEventType.BuildFailed, "filesystem", "fix", "CS0246"));
            rules.Add(new(AgentEventType.BuildFailed, "roslyn", "analyze"));
            rules.Add(new(AgentEventType.TestFailed, "dotnet", "diagnose"));
            rules.Add(new(AgentEventType.ValidationFailed, "code-orchestrator", "retry"));
            rules.Add(new(AgentEventType.HealthCritical, "thread", "escalate"));
            rules.Add(new(AgentEventType.HealthWarning, "aspire", "investigate"));
            rules.Add(new(AgentEventType.DeployFailed, "thread", "escalate"));
            await WriteStateAsync(ct);
        }
    }

    public Task<RoutingResult?> RouteAsync(TaskEvent evt, CancellationToken ct = default)
    {
        foreach (var rule in rules)
        {
            if (rule.EventAction != evt.Action)
                continue;

            if (rule.ErrorCodePattern is not null
                && evt.Result.Contains(rule.ErrorCodePattern, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<RoutingResult?>(new(rule.TargetAgentType, rule.Action, evt.Result));

            if (rule.ErrorCodePattern is null)
                return Task.FromResult<RoutingResult?>(new(rule.TargetAgentType, rule.Action, evt.Result));
        }

        return Task.FromResult<RoutingResult?>(null);
    }

    public Task<IReadOnlyList<RoutingRule>> GetRulesAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RoutingRule>>(rules.ToList());

    public async Task AddRuleAsync(RoutingRule rule, CancellationToken ct = default)
    {
        rules.Add(rule);
        await WriteStateAsync(ct);
    }

    public async Task RemoveRuleAsync(string eventAction, string? errorCodePattern = null, CancellationToken ct = default)
    {
        for (var i = rules.Count - 1; i >= 0; i--)
        {
            if (rules[i].EventAction == eventAction
                && rules[i].ErrorCodePattern == errorCodePattern)
            {
                rules.RemoveAt(i);
            }
        }
        await WriteStateAsync(ct);
    }
}
