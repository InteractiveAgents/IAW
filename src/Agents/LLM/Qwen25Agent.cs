using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace IAW.Agents.LLM;

public class Qwen25Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Qwen25>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : global::IAW.Core.LLM(state, eventLog, chatClient, history, trackingItems), IQwen25
{
    protected override string DisplayName => Qwen25.Instance.DisplayName;
}
