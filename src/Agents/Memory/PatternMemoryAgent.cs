using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace IAW.Agents.Memory;

public class PatternMemoryAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::IAW.Core.Memory(state, eventLog, chatClient, history, trackingItems, memories, embedder), IPatternMemory
{
    protected override string CollectionName => "iaw-pattern-memory";
    protected override string DisplayName => "Pattern Memory";
    protected override string Instructions =>
        "You manage code patterns, design patterns, and recurring solutions. " +
        "Learn which patterns work well and recommend them for similar problems.";
}
