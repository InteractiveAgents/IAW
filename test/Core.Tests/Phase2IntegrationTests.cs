using Core.Contracts;
using Core.Contracts.Events;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class Phase2IntegrationTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task EventRouter_RoutesFailure_AndLedgerTracksIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("route-flow");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        var router = Cluster.GrainFactory.GetGrain<IEventRouter>("global");

        var failEvent = new TaskEvent(
            "DotNet", AgentEventType.BuildFailed, "CS0246: ThemeToggle not found", null, DateTimeOffset.UtcNow);

        await ledger.AppendAsync(failEvent, ct);

        var routing = await router.RouteAsync(failEvent, ct);
        Assert.NotNull(routing);
        Assert.Equal("filesystem", routing!.TargetAgentType);

        await ledger.AppendAsync(new TaskEvent(
            "Router", AgentEventType.StepCompleted,
            $"routed to {routing.TargetAgentType}", routing.Action, DateTimeOffset.UtcNow), ct);

        var events = await ledger.GetEventsAsync(ct);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task ApprovalGate_FullWorkflow()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = Cluster.GrainFactory.GetGrain<IApprovalGate>(UniqueId("workflow"));

        await gate.RequestAsync(new ApprovalRequest(
            "deploy-1", "Deploy self-improvement fix?",
            new List<string> { "Yes", "No" }, "safe-deployer"), ct);

        var pending = await gate.GetPendingAsync(ct);
        Assert.Single(pending);

        await gate.ResolveAsync("deploy-1", new ApprovalDecision("Yes", "approved by user"), ct);

        var result = await gate.GetResultAsync("deploy-1", ct);
        Assert.Equal("Yes", result!.Choice);

        var stillPending = await gate.GetPendingAsync(ct);
        Assert.Empty(stillPending);
    }

    [Fact]
    public async Task FullFlow_Ledger_Router_Approval()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("full-flow");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        var router = Cluster.GrainFactory.GetGrain<IEventRouter>("global");
        var gate = Cluster.GrainFactory.GetGrain<IApprovalGate>(UniqueId("full-gate"));

        // step 1: agent does work, publishes to ledger
        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted, "found N+1 query in ReportsController", null, DateTimeOffset.UtcNow), ct);

        // step 2: agent proposes fix, requests approval
        await gate.RequestAsync(new ApprovalRequest(
            "fix-n1", "Apply .Include() + .AsNoTracking() fix?",
            new List<string> { "Yes", "No", "Show Diff" }, "roslyn"), ct);

        await ledger.AppendAsync(new TaskEvent(
            "System", AgentEventType.ApprovalRequested, "approval requested: fix-n1 Apply N+1 fix?", null, DateTimeOffset.UtcNow), ct);

        // step 3: user approves
        await gate.ResolveAsync("fix-n1", new ApprovalDecision("Yes", "approved"), ct);

        // step 4: verify everything
        var decision = await gate.GetResultAsync("fix-n1", ct);
        Assert.Equal("Yes", decision!.Choice);

        var events = await ledger.GetEventsAsync(ct);
        Assert.Equal(2, events.Count);

        var block = await ledger.GetContextBlockAsync(ct: ct);
        Assert.Contains("N+1 query", block);
        Assert.Contains("approval", block.ToLowerInvariant());
    }
}
