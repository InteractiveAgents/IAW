namespace Core.Registry;

public interface IAgentRegistry : IGrainWithStringKey
{
    Task RegisterAsync(AgentRecord record, CancellationToken ct = default);
    Task<List<AgentCandidate>> SearchAsync(string query, string? namespaceFilter = null, int top = 15, CancellationToken ct = default);
    Task<List<AgentRecord>> GetAllAsync(CancellationToken ct = default);
    Task<string> ToPromptStringAsync(CancellationToken ct = default);
    Task<AgentRecord?> GetByAgentTypeAsync(string agentType, CancellationToken ct = default);
}