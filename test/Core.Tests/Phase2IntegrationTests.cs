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
}
