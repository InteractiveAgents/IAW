namespace Core.Registry;

public interface IAgentRegistryGrain : IGrainWithStringKey
{
    Task RegisterAsync(AgentRegistration registration);
    Task UnregisterAsync(string agentType);
    Task<IReadOnlyList<AgentRegistration>> GetAllAsync();
    Task<IReadOnlyList<AgentRegistration>> QueryAsync(AgentQuery query);
    Task<AgentRegistration?> GetByTypeAsync(string agentType);
}
