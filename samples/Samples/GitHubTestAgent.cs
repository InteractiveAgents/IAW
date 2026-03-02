using Core;
using Core.AI;
using Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Samples;

public interface IGitHubTestAgent : IAgent;

public sealed class GitHubTestAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking,
    [Llm<GitHubGpt4oMini>] IChatClient chatClient)
    : Agent(values, history, events, subscriptions, notifications, tracking), IGitHubTestAgent
{
    public override string DisplayName => "GitHub Test Agent";
    public override string SystemPrompt => "You are a helpful test agent. Keep responses under 50 words.";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        Activate(chatClient);
    }
}
