using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

namespace IAW.Agents.Memory;

public class PatternMemoryAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<PatternMemoryAgent> logger)
    : MemoryAgentBase(durableState, chatClient, memories, embedder, logger), IPatternMemory
{
    protected override string CollectionName => "iaw-pattern-memory";
    protected override string DisplayName => "Pattern Memory";

    public static string AgentDescription => "Stores proven code and design patterns, recommending them for similar problems via vector search.";
    public static string[] AgentCapabilities => ["memory", "patterns", "design", "search", "recall", "vector"];
    protected override string Instructions =>
        "You are Pattern Memory, the IAW team's catalog of proven code and design patterns. " +
        "Store patterns that work well and recommend them for similar problems when queried.";

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
