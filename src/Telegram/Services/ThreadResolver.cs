using Core.Contracts;
using IAW.Agents.Orchestration;

namespace TelegramClient.Services;

// shared helper — resolves a Telegram user + topic into an IThread grain
public static class ThreadResolver
{
    public static async Task<(IThread Thread, string Slug)> ResolveAsync(
        IClusterClient clusterClient, long telegramId, int? topicId, CancellationToken ct)
    {
        var userProfileId = telegramId.ToString();
        var userProfile = clusterClient.GetGrain<IUserProfile>(userProfileId);
        var topicKey = topicId?.ToString() ?? "general";

        var projectSlug = await userProfile.ResolveProject(topicKey, ct);
        if (projectSlug is null)
        {
            projectSlug = topicId is null ? "general" : $"topic-{topicId}";
            await userProfile.RegisterProject(projectSlug, topicKey, ct);
        }

        var grainId = $"{userProfileId}/{projectSlug}";
        return (clusterClient.GetGrain<IThread>(grainId), projectSlug);
    }
}
