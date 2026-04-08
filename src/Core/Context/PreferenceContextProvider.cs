using Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public class PreferenceContextProvider(
    IGrainFactory grainFactory,
    string preferenceAgentId,
    string? categoryFilter = null,
    ILogger<PreferenceContextProvider>? logger = null) : IAgentContextProvider
{
    public string Name => "user-preferences";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        try
        {
            var prefAgent = grainFactory.GetGrain<IPreference>(preferenceAgentId);
            var rules = await prefAgent.GetRulesAsync(categoryFilter, ct);

            if (rules.Count == 0)
                return [];

            return rules.Select(r =>
            {
                var reason = r.Reason is not null ? $" (reason: {r.Reason})" : "";
                return $"[preference:{r.Category}] {r.Rule}{reason}";
            }).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { logger?.LogWarning(ex, "Preference context provider failed for {AgentId}", preferenceAgentId); return []; }
    }
}
