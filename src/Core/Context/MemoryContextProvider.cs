using Core.Contracts;

namespace Core.Context;

public class MemoryContextProvider(IGrainFactory grainFactory, string[] memoryAgentIds) : IAgentContextProvider
{
    public string Name => "Memory";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        var context = new List<string>();

        foreach (var memoryId in memoryAgentIds)
        {
            try
            {
                var memoryAgent = grainFactory.GetGrain<IAgent>(memoryId);
                var response = await memoryAgent.GetResponse($"Search for memories relevant to: {prompt}", ct);
                if (!string.IsNullOrWhiteSpace(response))
                    context.Add($"[{memoryId}] {response}");
            }
            catch
            {
                // memory agent unavailable — skip
            }
        }

        return context;
    }
}
