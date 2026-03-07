using Core.V3.Communication;
using Core.V3.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Core.V3.Samples;

public interface IInfraMonitorAgent : IAgent, ITrackableAgent;

public class InfraMonitorAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IInfraMonitorAgent,
      IStreamProducer<HealthCheckEvent>
{
    protected override string Instructions =>
        "You monitor infrastructure health. Check service endpoints and report issues.";

    protected override string DisplayName => "Infrastructure Monitor";

    public async Task PublishToStreamAsync(HealthCheckEvent evt, CancellationToken ct = default)
    {
        await PublishTypedAsync(evt, ct);
    }

    protected override async Task OnTrackingDueAsync(TrackingItem item, CancellationToken ct)
    {
        await base.OnTrackingDueAsync(item, ct);
        await PublishToStreamAsync(new HealthCheckEvent(
            this.GetPrimaryKeyString(),
            Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow,
            item.Description,
            true,
            null), ct);
    }
}
