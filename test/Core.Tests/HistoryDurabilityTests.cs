using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class HistoryDurabilityTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task History_PersistsAfterEachMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("hist-persist"));
        await agent.GetResponse("Hello", ct);
        var history = await agent.GetHistory(ct);
        Assert.True(history.Count >= 2, $"Expected at least 2 messages, got {history.Count}");
    }

    [Fact]
    public async Task History_SurvivesGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var agentId = UniqueId("hist-deactivate");
        var agent = Agent(agentId);

        await agent.GetResponse("Remember this message", ct);
        var historyBefore = await agent.GetHistory(ct);
        var countBefore = historyBefore.Count;
        Assert.True(countBefore >= 2);

        // force deactivation of all grains via the management grain
        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(500, ct);

        var agent2 = Agent(agentId);
        var historyAfter = await agent2.GetHistory(ct);

        Assert.Equal(countBefore, historyAfter.Count);
        Assert.Contains(historyAfter, m => m.Content!.Contains("Remember this message"));
    }
}
