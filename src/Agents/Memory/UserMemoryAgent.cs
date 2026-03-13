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
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<UserMemoryAgent> logger)
    : global::Core.Memory(durableState, chatClient, memories, embedder, logger), IUserMemory
{
    protected override string CollectionName => "iaw-user-memory";
    protected override string DisplayName => "User Memory";
    protected override string Instructions =>
        "You are User Memory, the IAW team's long-term store for personal facts, preferences, and corrections. " +
        "Extract personal information from conversations and store it. Search and surface relevant memories when queried.";
}
