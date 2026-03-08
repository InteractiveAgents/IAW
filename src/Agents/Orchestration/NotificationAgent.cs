using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Orchestration;

public class NotificationAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<global::Core.Contracts.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), INotification
{
    protected override string DisplayName => "Notification Hub";
    protected override string Instructions => "Aggregates events and delivers notifications";

    public override async Task HandleEvent(AgentEvent agentEvent, CancellationToken ct = default)
    {
        await PublishAsync("notification.delivered", new Dictionary<string, object>
        {
            ["OriginalEvent"] = agentEvent.EventName,
            ["Source"] = agentEvent.SourceAgentId,
            ["Payload"] = agentEvent.Payload,
            ["DeliveredAt"] = DateTimeOffset.UtcNow
        }, ct);
    }
}
