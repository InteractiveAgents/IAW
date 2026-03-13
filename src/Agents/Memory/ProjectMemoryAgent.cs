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
    : global::Core.Memory(durableState, chatClient, memories, embedder, logger), IProjectMemory
{
    protected override string CollectionName => "iaw-project-memory";
    protected override string DisplayName => "Project Memory";
    protected override string Instructions =>
        "You manage project conventions, architecture decisions, and team agreements. " +
        "Track how the project evolves and remember key design choices.";
}
