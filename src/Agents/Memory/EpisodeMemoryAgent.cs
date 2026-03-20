using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

namespace IAW.Agents.Memory;

public class EpisodeMemoryAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<EpisodeMemoryAgent> logger)
    : MemoryAgentBase(durableState, chatClient, memories, embedder, logger), IEpisodeMemory
{
    protected override string CollectionName => "iaw-episode-memory";
    protected override string DisplayName => "Episode Memory";

    public static string AgentDescription => "Records task workflows and outcomes, enabling retrieval of past episode context via vector search.";
    public static string[] AgentCapabilities => ["memory", "episode", "workflow", "search", "recall", "vector"];
    protected override string Instructions =>
        "You are Episode Memory, the IAW team's record of task workflows and outcomes. " +
        "Store what steps were taken, their results, and how tasks completed. Surface relevant episodes when queried.";

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
