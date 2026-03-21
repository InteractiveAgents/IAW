using Core.UI;
using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class TelegramUIAgentTests : AgentTest<TelegramUIAgent>
{
    [Fact]
    public async Task FormatResponse_ReturnsRichOutput()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Cluster.Client.GetGrain<ITelegramUI>(UniqueId("fmt"));

        var result = await agent.FormatResponse("Hello world", ct);

        Assert.NotNull(result);
        Assert.NotEmpty(result.FormattedText);
    }

    [Fact]
    public async Task FormatResponse_EmptyText_ReturnsEmptyParts()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Cluster.Client.GetGrain<ITelegramUI>(UniqueId("empty"));

        var result = await agent.FormatResponse("", ct);

        Assert.NotNull(result);
        Assert.Empty(result.Parts);
    }
}
