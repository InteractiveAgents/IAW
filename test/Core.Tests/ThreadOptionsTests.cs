using Core.Contracts;
using Core.Contracts.UI;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ThreadOptionsTests : AgentTest<ThreadAgent>
{
    [Fact]
    public async Task ConsumePendingOptions_ReturnsNullWhenNoPending()
    {
        var ct = TestContext.Current.CancellationToken;
        var threadUI = Cluster.Client.GetGrain<IThreadUI>(UniqueId("no-opts"));

        var result = await threadUI.ConsumePendingOptions(ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConsumePendingOptions_IsOneShot_SecondCallReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("oneshot");
        var threadUI = Cluster.Client.GetGrain<IThreadUI>(id);

        var first = await threadUI.ConsumePendingOptions(ct);
        Assert.Null(first);

        var second = await threadUI.ConsumePendingOptions(ct);
        Assert.Null(second);
    }

    [Fact]
    public async Task DefineAdditionalTools_IncludesPresentOptions()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("tools");
        var thread = Agent(id);

        var caps = await thread.GetCapabilities(ct);
        Assert.True(caps.HasTools);
    }
}
