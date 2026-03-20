using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

namespace IAW.Agents.Memory;

public class UserMemoryAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<UserMemoryAgent> logger)
    : MemoryAgentBase(durableState, chatClient, memories, embedder, logger), IUserMemory
{
    protected override string CollectionName => "iaw-user-memory";
    protected override string DisplayName => "User Memory";

    public static string AgentDescription => "Stores personal facts, preferences, and corrections about users, enabling personalized long-term recall.";
    public static string[] AgentCapabilities => ["memory", "user", "preferences", "personal", "search", "recall"];
    protected override string Instructions =>
        "You are User Memory, the IAW team's long-term store for personal facts, preferences, and corrections. " +
        "Extract personal information from conversations and store it. Search and surface relevant memories when queried.";

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        if (!ScheduledJobs.ContainsKey("memory-maintenance"))
            await ScheduleRecurringJob("memory-maintenance", TimeSpan.FromHours(24), "memory-maintenance", ct);
    }

    protected override async Task OnScheduledJobDueAsync(ScheduledJobItem job, CancellationToken ct)
    {
        if (job.Name == "memory-maintenance")
        {
            try
            {
                await Decay(0.95f);
                await Consolidate();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Memory maintenance job failed");
            }
            ScheduledJobs[job.Name] = job with { LastRunAt = DateTimeOffset.UtcNow };
        }
        else
        {
            await base.OnScheduledJobDueAsync(job, ct);
        }
    }
}
