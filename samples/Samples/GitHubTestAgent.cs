using IAW.Core;
using Core.AI;
using Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Core;

namespace Samples;

public interface IGitHubTestAgent : IAgent;

public sealed class GitHubTestAgent(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking,
    [Llm<GitHubGpt4oMini>] IChatClient chatClient)
    : Agent(messages, memory, events, subscriptions, notifications, tracking), IGitHubTestAgent
{
    public override string DisplayName => "GitHub Test Agent";
    public override string SystemPrompt => "You are a helpful test agent. Keep responses under 50 words.";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        Activate(chatClient);
    }
}
