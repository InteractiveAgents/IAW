using Core.Contracts;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ApprovalGateTests : AgentTest<TestAgent>
{
    private IApprovalGate Gate(string id) => Cluster.GrainFactory.GetGrain<IApprovalGate>(id);

    [Fact]
    public async Task RequestAndApprove_ReturnsResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = Gate(UniqueId("approve"));

        await gate.RequestAsync(new ApprovalRequest(
            "deploy-fix", "Apply N+1 query fix?",
            new List<string> { "Yes", "No", "Show Diff" }, "test-agent"), ct);

        var pending = await gate.GetPendingAsync(ct);
        Assert.Single(pending);
        Assert.Equal("deploy-fix", pending[0].Id);

        await gate.ResolveAsync("deploy-fix", new ApprovalDecision("Yes", "looks good"), ct);

        var result = await gate.GetResultAsync("deploy-fix", ct);
        Assert.NotNull(result);
        Assert.Equal("Yes", result!.Choice);

        var noPending = await gate.GetPendingAsync(ct);
        Assert.Empty(noPending);
    }

    [Fact]
    public async Task AwaitApproval_BlocksUntilResolved()
    {
        var ct = TestContext.Current.CancellationToken;
        var gate = Gate(UniqueId("await"));

        await gate.RequestAsync(new ApprovalRequest(
            "risky-op", "Delete production branch?",
            new List<string> { "Yes", "No" }, "test-agent"), ct);

        var awaitTask = gate.AwaitDecisionAsync("risky-op", ct);
        Assert.False(awaitTask.IsCompleted);

        await gate.ResolveAsync("risky-op", new ApprovalDecision("No", "too risky"), ct);

        var result = await awaitTask;
        Assert.Equal("No", result.Choice);
        Assert.Equal("too risky", result.Notes);
    }

    [Fact]
    public async Task Approval_SurvivesGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("durable-gate");
        var gate = Gate(id);

        await gate.RequestAsync(new ApprovalRequest(
            "deploy-v2", "Deploy version 2?", new List<string> { "Yes", "No" }, "deployer"), ct);

        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(2000, ct);

        var gate2 = Gate(id);
        var pending = await gate2.GetPendingAsync(ct);
        Assert.Single(pending);
        Assert.Equal("deploy-v2", pending[0].Id);

        await gate2.ResolveAsync("deploy-v2", new ApprovalDecision("Yes", "approved"), ct);
        var result = await gate2.GetResultAsync("deploy-v2", ct);
        Assert.Equal("Yes", result!.Choice);
    }
}
