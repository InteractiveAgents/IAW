using Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public class MemoryContextProvider(IReadOnlyList<IMemoryAgent> memoryAgents, ILogger<MemoryContextProvider>? logger = null) : IAgentContextProvider
{
    public string Name => "Memory";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        var context = new List<string>();

        foreach (var memoryAgent in memoryAgents)
        {
            try
            {
                var results = await memoryAgent.SearchAsync(prompt, 3, ct);
                foreach (var entry in results)
                    context.Add($"[memory] {entry.Content}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Memory context provider failed for agent {AgentId}", agentId);
            }
        }

        return context;
    }
}