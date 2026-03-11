using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace IAW.Agents.LLM;

public class Gpt4oAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Gpt4o>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : global::IAW.Core.LLM(state, eventLog, chatClient, history, trackingItems), IGpt4o
{
    protected override string DisplayName => Gpt4o.Instance.DisplayName;
}
