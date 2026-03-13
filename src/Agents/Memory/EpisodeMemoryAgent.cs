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
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<EpisodeMemoryAgent> logger)
    : global::Core.Memory(durableState, chatClient, memories, embedder, logger), IEpisodeMemory
{
    protected override string CollectionName => "iaw-episode-memory";
    protected override string DisplayName => "Episode Memory";
    protected override string Instructions =>
        "You manage task episodes and workflow sequences. " +
        "Remember what steps were taken, their outcomes, and how tasks were completed.";
}
