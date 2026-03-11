using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace IAW.Agents.Memory;

public class EpisodeMemoryAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::IAW.Core.Memory(state, eventLog, chatClient, history, trackingItems, memories, embedder), IEpisodeMemory
{
    protected override string CollectionName => "iaw-episode-memory";
    protected override string DisplayName => "Episode Memory";
    protected override string Instructions =>
        "You manage task episodes and workflow sequences. " +
        "Remember what steps were taken, their outcomes, and how tasks were completed.";
}
