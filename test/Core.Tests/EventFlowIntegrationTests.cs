using Core.Contracts;
using Core.Contracts.Events;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class EventFlowIntegrationTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task MultiAgent_SharesContext_ViaLedger()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("multi-agent");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted,
            "analyzed workspace: 12 files, 3 interfaces", null, DateTimeOffset.UtcNow), ct);

        await ledger.AppendAsync(new TaskEvent(
            "FileSystem", AgentEventType.FileCreated,
            "created UserSettings.razor", "src/DevUI/Pages/", DateTimeOffset.UtcNow), ct);

        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded,
            "build passed, 0 warnings", null, DateTimeOffset.UtcNow), ct);

        var block = await ledger.GetContextBlockAsync(maxEvents: 15, ct);

        Assert.False(string.IsNullOrEmpty(block));

        Assert.Contains("Roslyn", block);
        Assert.Contains("FileSystem", block);
        Assert.Contains("DotNet", block);
        Assert.Contains("UserSettings.razor", block);

        Assert.True(block.Length < 400, $"Context block too large for 3 events: {block.Length} chars");
    }

    [Fact]
    public async Task Ledger_Events_Are_Durable()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("durable-flow");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Finance", AgentEventType.StepCompleted,
            "categorized 147 transactions", "8 categories", DateTimeOffset.UtcNow), ct);

        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(500, ct);

        var ledger2 = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        var events = await ledger2.GetEventsAsync(ct);
        Assert.Single(events);
        Assert.Equal("Finance", events[0].Agent);
    }

    [Fact]
    public async Task HistoryAndLedger_BothDurable()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("both-durable");
        var taskId = UniqueId("both-task");

        var agent = Agent(agentId);
        await agent.GetResponse("Hello world", ct);

        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        await ledger.AppendAsync(new TaskEvent(
            "TestAgent", AgentEventType.StepCompleted, "responded to user", null, DateTimeOffset.UtcNow), ct);

        var historyBefore = await agent.GetHistory(ct);
        Assert.True(historyBefore.Count >= 2, $"Pre-deactivation history should have >= 2 messages, got {historyBefore.Count}");

        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(1000, ct);

        var agent2 = Agent(agentId);
        var history = await agent2.GetHistory(ct);
        Assert.True(history.Count >= 2, $"Post-deactivation history should have >= 2 messages, got {history.Count}");

        var ledger2 = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);
        var events = await ledger2.GetEventsAsync(ct);
        Assert.Single(events);
    }
}
