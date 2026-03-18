using Core.Contracts;
using Orleans.Journaling;

namespace Core.Registry;

[GrainType(IAWConstants.GrainTypes.AgentRegistry)]
public class AgentRegistryGrain(
    [Memory("registrations")] IDurableDictionary<string, AgentRegistration> registrations)
    : DurableGrain, IAgentRegistryGrain
{
    public async Task RegisterAsync(AgentRegistration registration)
    {
        registrations[registration.AgentType] = registration;
        await WriteStateAsync();
    }

    public async Task UnregisterAsync(string agentType)
    {
        if (registrations.ContainsKey(agentType))
            registrations.Remove(agentType);
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<AgentRegistration>> GetAllAsync()
        => Task.FromResult<IReadOnlyList<AgentRegistration>>([.. registrations.Values]);

    public Task<IReadOnlyList<AgentRegistration>> QueryAsync(AgentQuery query)
    {
        var results = registrations.Values.AsEnumerable();
        if (query.Publishes is { Length: > 0 } pubs)
            results = results.Where(r => pubs.Any(p => r.Publishes.Contains(p)));
        if (query.Subscribes is { Length: > 0 } subs)
            results = results.Where(r => subs.Any(s => r.Subscribes.Contains(s)));
        return Task.FromResult<IReadOnlyList<AgentRegistration>>([.. results]);
    }

    public Task<AgentRegistration?> GetByTypeAsync(string agentType)
        => Task.FromResult(registrations.TryGetValue(agentType, out var reg) ? reg : null);
}
