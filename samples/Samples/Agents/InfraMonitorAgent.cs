using IAW.Core;
using IAW.Core.Communication;
using IAW.Core.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Samples.Agents;

public interface IInfraMonitorAgent : IAgent;

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
}
