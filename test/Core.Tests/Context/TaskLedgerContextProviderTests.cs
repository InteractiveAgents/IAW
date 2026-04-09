using Core.Context;
using Core.Contracts;
using Core.Contracts.Events;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.Context;

public class TaskLedgerContextProviderTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetContext_ReturnsLedgerEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var taskId = UniqueId("ctx-task");
        var ledger = Cluster.GrainFactory.GetGrain<ITaskLedger>(taskId);

        await ledger.AppendAsync(new TaskEvent(
            "Roslyn", AgentEventType.StepCompleted, "analyzed 12 files", null, DateTimeOffset.UtcNow), ct);
        await ledger.AppendAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildSucceeded, "0 warnings", null, DateTimeOffset.UtcNow), ct);

        var provider = new TaskLedgerContextProvider(Cluster.GrainFactory, taskId);
        var context = await provider.GetContextAsync("test-agent", "build the project", ct);

        Assert.NotEmpty(context);
        var combined = string.Join("\n", context);
        Assert.Contains("Roslyn", combined);
        Assert.Contains("DotNet", combined);
    }

    [Fact]
    public async Task GetContext_ReturnsEmpty_WhenNoEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new TaskLedgerContextProvider(Cluster.GrainFactory, UniqueId("empty-task"));
        var context = await provider.GetContextAsync("test-agent", "hello", ct);

        Assert.Empty(context);
    }
}
