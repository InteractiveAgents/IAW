using Core;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

namespace IAW.Agents.Memory;

public class ProjectMemoryAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<ProjectMemoryAgent> logger)
    : MemoryAgentBase(durableState, chatClient, memories, embedder, logger), IProjectMemory
{
    protected override string CollectionName => "iaw-project-memory";
    protected override string DisplayName => "Project Memory";
    protected override string Instructions =>
        "You are Project Memory, the IAW team's record of conventions, architecture decisions, and agreements. " +
        "Track how the project evolves and surface relevant decisions when queried.";

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await base.OnActivateAsync(ct);
        await this.RegisterOrUpdateReminder("memory-maintenance", TimeSpan.FromHours(24), TimeSpan.FromHours(24));
    }

    public override async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName == "memory-maintenance")
        {
            try
            {
                await Decay(0.95f);
                await Consolidate();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Memory maintenance reminder failed");
            }
        }
        else
        {
            await base.ReceiveReminder(reminderName, status);
        }
    }
}
