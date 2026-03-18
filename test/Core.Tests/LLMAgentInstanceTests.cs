using IAW.Agents.LLM;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class LLMAgentInstanceTests : AgentTest<Opus46Agent>
{
    [Fact]
    public async Task Opus46Agent_responds_via_GetResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("opus46");
        var response = await agent.GetResponse("hello", ct);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task Opus46Agent_metadata_shows_correct_display_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("opus46");
        var metadata = await agent.GetMetadata(ct);
        Assert.Equal("Claude Opus 4.6", metadata.DisplayName);
    }
}
