using Core;
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

    // The mock LLM returns "mock-response" which is not valid JSON, so ParseJudgment
    // falls through to the "ask" default. That's exactly what exercises the pending-prompt
    // path end-to-end without needing a bespoke mock.
    [Fact]
    public async Task Authorize_AskJudgment_PublishesApprovalRequestedEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = UniqueId("42");
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(userId);

        var request = BuildRequest(userId, "RunShellCommand", "{\"cmd\":\"ls\"}");
        var authorizeTask = approver.Authorize(request, ct);

        var approvalId = await WaitForApprovalRequestedAsync(approver, ct, authorizeTask);
        Assert.NotNull(approvalId);

        // Release the waiter so the test cleans up.
        await approver.ResolveApproval(approvalId!, ApprovalDecisionKeys.Deny, ct);
        var decision = await authorizeTask;

        Assert.Equal(AuthorizationOutcome.Deny, decision.Outcome);
    }

    [Fact]
    public async Task ResolveApproval_AllowUser_WritesMemo_SubsequentCallHitsMemo()
    {
        var ct = TestContext.Current.CancellationToken;
        var userId = UniqueId("7");
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(userId);

        var request = BuildRequest(userId, "GitStatus", "{\"path\":\"/workspace\"}");

        var firstAuthorize = approver.Authorize(request, ct);
        var approvalId = await WaitForApprovalRequestedAsync(approver, ct, firstAuthorize);
        Assert.NotNull(approvalId);

        await approver.ResolveApproval(approvalId!, ApprovalDecisionKeys.AllowUser, ct);
        var firstDecision = await firstAuthorize;
        Assert.Equal(AuthorizationOutcome.Allow, firstDecision.Outcome);

        // Second identical call must hit the memo and NOT generate a new pending entry.
        var eventsBefore = (await approver.GetEventLog(ct))
            .Count(e => e.EventName == IAWConstants.Events.ApprovalRequested);

        var secondDecision = await approver.Authorize(request, ct);
        Assert.Equal(AuthorizationOutcome.Allow, secondDecision.Outcome);

        var eventsAfter = (await approver.GetEventLog(ct))
            .Count(e => e.EventName == IAWConstants.Events.ApprovalRequested);
        Assert.Equal(eventsBefore, eventsAfter);
    }

    [Fact]
    public async Task ResolveApproval_RejectsForeignUser()
    {
        var ct = TestContext.Current.CancellationToken;
        var ownerId = UniqueId("11");
        var approver = Cluster.GrainFactory.GetGrain<IApprover>(ownerId);

        var request = BuildRequest(ownerId, "WriteFile", "{\"path\":\"a.txt\"}");
        var authorizeTask = approver.Authorize(request, ct);
        var approvalId = await WaitForApprovalRequestedAsync(approver, ct, authorizeTask);
        Assert.NotNull(approvalId);

        // Seed a foreign-owner pending entry directly through the state bag so we can
        // exercise the mismatch branch. We do this by creating a fake entry via the same
        // approver grain mechanism: call AddPolicy so the grain activates, then tamper by
        // resolving with a crafted foreign id — because ResolveApproval looks up by approvalId,
        // we simulate foreign ownership by using a different Approver grain to resolve the
        // first approver's pending id. Different grain keys => pending.UserId != grainUserId,
        // so the second approver must refuse.
        var foreignApprover = Cluster.GrainFactory.GetGrain<IApprover>(UniqueId("99"));
        // Call resolve on the foreign approver with the real approval id; since the pending
        // entry is not in the foreign approver's state, it's a no-op (no pending found).
        await foreignApprover.ResolveApproval(approvalId!, ApprovalDecisionKeys.AllowUser, ct);

        // The real approver still has the pending entry and is still waiting.
        Assert.False(authorizeTask.IsCompleted);

        // Clean up.
        await approver.ResolveApproval(approvalId!, ApprovalDecisionKeys.Deny, ct);
        var decision = await authorizeTask;
        Assert.Equal(AuthorizationOutcome.Deny, decision.Outcome);
    }

    static ToolAuthorizationRequest BuildRequest(string agentIdHead, string toolName, string argsJson) =>
        new(
            AgentId: $"{agentIdHead}/t/IFoo",
            AgentDisplayName: "TestAgent",
            ToolName: toolName,
            ArgumentsJson: argsJson,
            RecentMessages: Array.Empty<string>());

    static async Task<string?> WaitForApprovalRequestedAsync(IApprover approver, CancellationToken ct, Task? authorizeTask = null)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (authorizeTask is { IsFaulted: true })
                throw authorizeTask.Exception!.Flatten().InnerException ?? authorizeTask.Exception;

            var events = await approver.GetEventLog(ct);
            var evt = events.LastOrDefault(e => e.EventName == IAWConstants.Events.ApprovalRequested);
            if (evt is not null && evt.Payload.TryGetValue(IAWConstants.PayloadKeys.ApprovalId, out var id))
                return id;
            await Task.Delay(50, ct);
        }
        return null;
    }
}
