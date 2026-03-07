using IAW.Core.Communication;
using IAW.Core.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Core.Samples;

public interface IPersonalAssistantAgent : IAgent;

public class PersonalAssistantAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IPersonalAssistantAgent,
      IReceiver<ProgressNotification>,
      IBroadcaster<AssignTaskCommand>
{
    protected override string Instructions =>
        "You are a personal assistant. Decompose tasks and delegate to the engineering team.";

    protected override string DisplayName => "Personal Assistant";

    private readonly HashSet<string> _receivers = [];

    public async Task<MessageReceipt> ReceiveAsync(ProgressNotification evt, CancellationToken ct = default)
    {
        await GetResponse($"Progress update from {evt.SourceAgentId}: {evt.Step} — {evt.Status}", ct);
        return new MessageReceipt(true, this.GetPrimaryKeyString(), DateTimeOffset.UtcNow, null);
    }

    public Task<bool> CanReceiveAsync(CancellationToken ct = default) => Task.FromResult(true);

    public async Task<BroadcastResult> BroadcastAsync(AssignTaskCommand message, CancellationToken ct = default)
    {
        var delivered = 0;
        foreach (var id in _receivers)
        {
            try
            {
                var agent = GrainFactory.GetGrain<IAgent>(id);
                await agent.GetResponse($"Task assigned: {message.Description}", ct);
                delivered++;
            }
            catch { }
        }
        return new BroadcastResult(_receivers.Count, delivered, _receivers.Count - delivered, []);
    }

    public Task RegisterReceiverAsync(string receiverId) { _receivers.Add(receiverId); return Task.CompletedTask; }
    public Task UnregisterReceiverAsync(string receiverId) { _receivers.Remove(receiverId); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> GetReceiversAsync() => Task.FromResult<IReadOnlyList<string>>([.. _receivers]);
}
