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
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<PatternMemoryAgent> logger)
    : global::Core.Memory(durableState, chatClient, memories, embedder, logger), IPatternMemory
{
    protected override string CollectionName => "iaw-pattern-memory";
    protected override string DisplayName => "Pattern Memory";
    protected override string Instructions =>
        "You are Pattern Memory, the IAW team's catalog of proven code and design patterns. " +
        "Store patterns that work well and recommend them for similar problems when queried.";
}
