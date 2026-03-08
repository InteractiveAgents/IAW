using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Samples;

public interface IGitHubTestAgent : IAgent;

public sealed class GitHubTestAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<GitHubGpt4oMini>] IChatClient chatClient,
    [Memory("history")] IDurableList<Core.Contracts.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IGitHubTestAgent
{
    protected override string Instructions => "You are a helpful test agent. Keep responses under 50 words.";
    protected override string DisplayName => "GitHub Test Agent";
}
