using Core.Contracts;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class AgentSelectorTests : AgentTest<AgentSelectorAgent>
{
    [Fact]
    public async Task SelectAsync_ReturnsResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var selector = Cluster.GrainFactory.GetGrain<IAgentSelector>(UniqueId("sel"));
        var result = await selector.SelectAsync("run tests", ct);
        Assert.NotNull(result);
        Assert.True(
            result.Status is SelectionStatus.Ready or SelectionStatus.NeedsClarification or SelectionStatus.CannotHandle);
    }

    [Fact]
    public async Task SelectAsync_MockLlm_FallsBackGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var selector = Cluster.GrainFactory.GetGrain<IAgentSelector>(UniqueId("sel-fallback"));
        var result = await selector.SelectAsync("deploy to production", ct);
        Assert.NotNull(result);
        // mock LLM returns "mock-response" which isn't valid JSON, so parser falls back to CannotHandle
        Assert.Equal(SelectionStatus.CannotHandle, result.Status);
        Assert.NotNull(result.Plan);
    }

    [Fact]
    public async Task GetMetadata_ReturnsAgentSelectorDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("sel-meta"));
        var metadata = await agent.GetMetadata(ct);
        Assert.Equal("Agent Selector", metadata.DisplayName);
    }
}