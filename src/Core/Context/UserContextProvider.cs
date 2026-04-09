using Core.Contracts;
using Microsoft.Extensions.Logging;

namespace Core.Context;

public class UserContextProvider(IGrainFactory grainFactory, ILogger<UserContextProvider>? logger = null) : IAgentContextProvider
{
    public string Name => "user-profile";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct = default)
    {
        try
        {
            var telegramId = agentId.Split('/')[0];
            var userProfile = grainFactory.GetGrain<IUserProfile>(telegramId);
            var prefs = await userProfile.GetPreferences(ct);

            var context = new List<string>();
            foreach (var kvp in prefs)
            {
                if (!kvp.Key.StartsWith("fact:"))
                    context.Add($"[user] {kvp.Key}: {kvp.Value}");
            }

            var facts = await userProfile.RecallFacts(prompt, ct);
            foreach (var fact in facts)
                context.Add($"[user fact] {fact}");

            return context;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "User context provider failed for agent {AgentId}", agentId);
            return [];
        }
    }
}