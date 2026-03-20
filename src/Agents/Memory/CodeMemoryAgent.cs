using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

namespace IAW.Agents.Memory;

public class CodeMemoryAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<CodeMemoryAgent> logger)
    : MemoryAgentBase(durableState, chatClient, memories, embedder, logger), ICodeMemory
{
    protected override string CollectionName => "iaw-code-memory";
    protected override string DisplayName => "Code Memory";

    public static string AgentDescription => "Stores and retrieves code structure, dependency relationships, and implementation details via vector search.";
    public static string[] AgentCapabilities => ["memory", "code", "search", "recall", "vector", "embedding"];
    protected override string Instructions =>
        "You are Code Memory, the IAW team's record of code structure, dependencies, and implementation details. " +
        "Track code organization, dependency relationships, and key implementation decisions.";

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
