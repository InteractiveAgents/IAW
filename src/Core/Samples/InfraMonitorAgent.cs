using IAW.Core.Communication;
using IAW.Core.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Core.Samples;

public interface IInfraMonitorAgent : IAgent, ITrackableAgent;

public class InfraMonitorAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
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
