using Core.AI;
using Core.AI.Models;
using Core.Communication;
using Core.Contracts;
using IAW.Agents.Infrastructure;
using IAW.Agents.Messages;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace IAW.Agents.Orchestration;

public class DeployerAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<global::Core.Contracts.ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems),
      IDeployer,
      IStreamConsumer<TestsPassedEvent>
{
    protected override string DisplayName => "Deployer Agent";

    protected override string Instructions =>
        "You build release configurations, deploy to Aspire-orchestrated silos, and commit changes via Git. " +
        "Verify resource health after every deployment.";

    public async Task OnStreamEventAsync(TestsPassedEvent evt, StreamSequenceToken? token)
    {
        State["last-tests-passed-files"] = new StateEntry(
            "last-tests-passed-files", string.Join("|", evt.TestFiles));
        State["last-tests-passed-count"] = new StateEntry(
            "last-tests-passed-count", evt.Passed);
        await WriteStateAsync(default);

        var workspace = GetWorkspacePath() ?? ".";
        var taskId = Guid.NewGuid().ToString("N")[..8];

        var buildAgent = GrainFactory.GetGrain<IBuild>("build");
        var buildResult = await buildAgent.BuildAsync(workspace, "Release", default);

        if (!buildResult.Success)
        {
            await PublishAsync("deploy.failed", new Dictionary<string, object>
            {
                ["TaskId"] = taskId,
                ["Error"] = $"Release build failed: {string.Join("; ", buildResult.Diagnostics)}"
            }, default);
            return;
        }

        var aspireAgent = GrainFactory.GetGrain<IAspire>("aspire");
        var resources = await aspireAgent.ListResourcesAsync(default);
        var siloResource = resources.FirstOrDefault(r => r.Name.Contains("silo", StringComparison.OrdinalIgnoreCase));

        if (siloResource is not null)
        {
            await aspireAgent.RestartResourceAsync(siloResource.Name, default);
        }

        var gitAgent = GrainFactory.GetGrain<IGit>("git");
        await gitAgent.CommitAsync(workspace, $"Deploy after {evt.Passed} tests passed", default);

        var resourceName = siloResource?.Name ?? "unknown";
        await PublishAsync("deploy.succeeded", new Dictionary<string, object>
        {
            ["TaskId"] = taskId,
            ["ResourceName"] = resourceName
        }, default);
    }
}
