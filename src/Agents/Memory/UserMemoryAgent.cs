using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Memory;

public class UserMemoryAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::Core.Memory(durableState, chatClient, memories, embedder), IUserMemory
{
    protected override string CollectionName => "iaw-user-memory";
    protected override string DisplayName => "User Memory";
    protected override string Instructions =>
        "You manage user preferences, personal facts, and corrections. " +
        "Extract and remember personal information from conversations.";
}
