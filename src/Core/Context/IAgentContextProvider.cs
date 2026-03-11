namespace Core.Context;

public interface IAgentContextProvider
{
    string Name { get; }
    Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default);
}
