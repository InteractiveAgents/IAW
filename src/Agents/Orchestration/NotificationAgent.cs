using IAW.Core;
using IAW.Core.AI;
using IAW.Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Orchestration;

public class NotificationAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<IAW.Core.ChatMessage> history,
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
