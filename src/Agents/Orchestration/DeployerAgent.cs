using Core.AI;
using Core.AI.Models;
using Core.Communication;
using Core.Contracts;
using IAW.Agents.Infrastructure;
using IAW.Agents.Messages;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Streams;

namespace IAW.Agents.Orchestration;

public class DeployerAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient),
      IDeployer,
      IStreamConsumer<TestsPassedEvent>
{
    protected override string DisplayName => "Deployer";

    protected override string Instructions => """
        You are the Deployer, the IAW team's release and deployment specialist. Build, verify, deploy, and commit releases.

        CAPABILITIES:
        - Build projects in Release configuration
        - Query Aspire resource status
        - Restart application silos after deployment
        - Commit deployment changes via Git
        - Verify resource health post-deployment

        DEPLOYMENT PROCESS:
        1. Build in Release configuration and verify success
        2. Query Aspire for silo resource details
        3. Restart the silo resource if build succeeded
        4. Commit with message: "Deploy after [event context]"
        5. Verify resource is running and report status

        OUTPUT FORMAT:
        Success: "Deployment complete. Resource: {resourceName}, Status: running"
        Failure: "Deployment failed: {error}. Rolled back."

        RULES:
        - ALWAYS verify the Release build succeeds before attempting deployment
        - ALWAYS query resource health after restart (within 10 seconds)
        - If build fails, abort deployment and report the build error
        - If silo restart fails, report error and suggest manual intervention
        - Commit only after successful deployment
        - Report actual outcomes, not what could be done
        """;

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
