using Core.Contracts;
using Core.Contracts.Events;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class TaskLedgerTests : AgentTest<TestAgent>
{
    private ITaskLedger Ledger(string id) => Cluster.GrainFactory.GetGrain<ITaskLedger>(id);

    [Fact]
    public async Task Append_StoresEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("ledger"));

        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded, "0 warnings", null, DateTimeOffset.UtcNow), ct);

        var events = await ledger.GetEventsAsync(ct);
        Assert.Single(events);
        Assert.Equal("DotNet", events[0].Agent);
        Assert.Equal(AgentEventType.BuildSucceeded, events[0].Action);
    }

    [Fact]
    public async Task GetEvents_ReturnsInOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("order"));

        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted, "analyzed workspace", null, DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "FileSystem", AgentEventType.FileCreated, "created 3 files", null, DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded, "build passed", null, DateTimeOffset.UtcNow), ct);

        var events = await ledger.GetEventsAsync(ct);
        Assert.Equal(3, events.Count);
        Assert.Equal("Roslyn", events[0].Agent);
        Assert.Equal("FileSystem", events[1].Agent);
        Assert.Equal("DotNet", events[2].Agent);
    }

    [Fact]
    public async Task GetContextBlock_FormatsCompactly()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("context"));

        await ledger.AppendAsync(new TaskEvent(
            "FileSystem", AgentEventType.FileRead, "147 transactions", "bank.csv", DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "Finance", AgentEventType.StepCompleted, "categorized into 8 groups", null, DateTimeOffset.UtcNow), ct);

        var block = await ledger.GetContextBlockAsync(maxEvents: 10, ct);

        Assert.Contains("FileSystem", block);
        Assert.Contains("Finance", block);
        Assert.Contains("147 transactions", block);
        Assert.True(block.Length < 500, $"Context block too large: {block.Length} chars");
    }

    [Fact]
    public async Task GetContextBlock_TruncatesOldEventsWhenOverLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        var ledger = Ledger(UniqueId("truncate"));

        for (var i = 0; i < 20; i++)
        {
            await ledger.AppendAsync(new TaskEvent(
                $"Agent{i}", AgentEventType.StepCompleted, $"step {i} done", null, DateTimeOffset.UtcNow), ct);
        }

        var block = await ledger.GetContextBlockAsync(maxEvents: 5, ct);
        Assert.Contains("Agent19", block);
        Assert.Contains("Agent15", block);
        Assert.DoesNotContain("Agent0", block);
    }

    [Fact(Skip = "Journaling uses VolatileStateMachineStorageProvider — state does not survive deactivation by design")]
    public async Task Events_SurviveGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("durable");
        var ledger = Ledger(id);

        await ledger.AppendAsync(new TaskEvent(
            "Git", AgentEventType.CommitCreated, "abc1234", "feat: add auth", DateTimeOffset.UtcNow), ct);

        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(500, ct);

        var ledger2 = Ledger(id);
        var events = await ledger2.GetEventsAsync(ct);
        Assert.Single(events);
        Assert.Equal("Git", events[0].Agent);
    }
}
