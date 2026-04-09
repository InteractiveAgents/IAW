using Core.Contracts.Security;
using IAW.Agents.Security;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ApproverAgentTests : AgentTest<ApproverAgent>
{
    [Fact]
    public async Task AddPolicy_PersistsAndAppearsInList()
    {
        var ct = TestContext.Current.CancellationToken;
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(UniqueId("user-policy"));

        var result = await approver.AddPolicy("User", null, "Always allow dotnet build and test commands", ct);
        Assert.Contains("Policy added", result);

        var policies = await approver.ListPolicies(ct);
        Assert.Single(policies);
        Assert.Equal(AuthorizationScope.User, policies[0].Scope);
        Assert.Contains("dotnet build", policies[0].Rule);
    }

    [Fact]
    public async Task ListPolicies_EmptyForNewApprover()
    {
        var ct = TestContext.Current.CancellationToken;
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(UniqueId("user-empty"));

        var policies = await approver.ListPolicies(ct);
        Assert.Empty(policies);
    }

    [Fact]
    public async Task RemovePolicy_OnEmptyApprover_ReturnsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(UniqueId("user-remove-empty"));

        var result = await approver.RemovePolicy("anything", ct);
        Assert.Contains("No policies", result);
    }

    [Fact]
    public async Task ResolveApproval_NonExistentIdIsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(UniqueId("user-resolve"));

        // Must not throw when called with an unknown approval id.
        await approver.ResolveApproval("does-not-exist", ApprovalDecisionKeys.Deny, ct);
    }

    [Fact]
    public async Task AddPolicy_ThreadScope_StoresThreadId()
    {
        var ct = TestContext.Current.CancellationToken;
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(UniqueId("user-thread-scope"));

        await approver.AddPolicy("Thread", "my-thread-42", "Allow file reads in this conversation", ct);
        var policies = await approver.ListPolicies(ct);

        Assert.Single(policies);
        Assert.Equal(AuthorizationScope.Thread, policies[0].Scope);
        Assert.Equal("my-thread-42", policies[0].ThreadId);
    }
}
