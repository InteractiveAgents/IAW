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
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<CodeMemoryAgent> logger)
    : global::Core.Memory(durableState, chatClient, memories, embedder, logger), ICodeMemory
{
    protected override string CollectionName => "iaw-code-memory";
    protected override string DisplayName => "Code Memory";
    protected override string Instructions =>
        "You are Code Memory, the IAW team's record of code structure, dependencies, and implementation details. " +
        "Track code organization, dependency relationships, and key implementation decisions.";

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
