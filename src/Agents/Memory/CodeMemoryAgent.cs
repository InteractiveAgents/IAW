using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace IAW.Agents.Memory;

public class CodeMemoryAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::IAW.Core.Memory(state, eventLog, chatClient, history, trackingItems, memories, embedder), ICodeMemory
{
    protected override string CollectionName => "iaw-code-memory";
    protected override string DisplayName => "Code Memory";
    protected override string Instructions =>
        "You manage code structure, dependencies, and implementation details. " +
        "Track how code is organized, what depends on what, and key implementation decisions.";
}
