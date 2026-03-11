using Core.Contracts;
using ChatMessage = Core.Contracts.ChatMessage;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Core;

public abstract class LLM(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems)
{
    protected override string Instructions =>
        $"You are {DisplayName}. Answer directly and accurately.";
}
