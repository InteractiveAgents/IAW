using Core;
using Core.Communication;
using Core.Messages;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace Samples.Agents;

public interface ICIPipelineAgent : IAgent;

public class CIPipelineAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      ICIPipelineAgent,
      IStreamConsumer<CodeChangedEvent>,
      IStreamProducer<BuildCompletedEvent>
{
    protected override string Instructions =>
        "You are a CI/CD pipeline agent. When code changes arrive, run builds and tests, then publish results.";

    protected override string DisplayName => "CI/CD Pipeline";

    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        var result = await GetResponse($"Build and test files: {string.Join(", ", evt.FilePaths)}", AgentCancellation);
        var success = !result.Contains("error", StringComparison.OrdinalIgnoreCase);

        await PublishToStreamAsync(new BuildCompletedEvent(
            this.GetPrimaryKeyString(),
            evt.CorrelationId,
            DateTimeOffset.UtcNow,
            success,
            evt.CommitSha,
            result), AgentCancellation);
    }

    public async Task PublishToStreamAsync(BuildCompletedEvent evt, CancellationToken ct = default)
    {
        await PublishTypedAsync(evt, ct);
    }
}
