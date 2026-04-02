using Core.Contracts;
using Core.Registry;
using IAW.Agents.Web;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class PlaywrightAgentBasicTests : AgentTest<PlaywrightAgent>
{
    [Fact]
    public async Task GetMetadata_ReturnsPlaywrightDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("pw-meta"));
        var metadata = await agent.GetMetadata(ct);
        Assert.Equal("Playwright", metadata.DisplayName);
    }

    [Fact]
    public void GetMetadata_HasBrowserCapabilities()
    {
        var capabilities = AgentInterfaceMetadata.Capabilities<IPlaywright>();
        Assert.Contains("browser", capabilities);
        Assert.Contains("scrape", capabilities);
        Assert.Contains("web", capabilities);
    }

    [Fact]
    public async Task GetResponse_WithoutMcp_StillResponds()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("pw-resp"));
        var response = await agent.GetResponse("Hello", ct);
        Assert.Equal("mock-response", response);
    }

    [Fact]
    public async Task ScrapePageAsync_WithoutMcp_ReturnsLlmResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IPlaywright>(UniqueId("pw-scrape"));
        var result = await grain.ScrapePageAsync("https://example.com", "get the title", ct);
        Assert.Equal("mock-response", result);
    }

    [Fact]
    public async Task ExtractDataAsync_ReturnsNonEmptyString()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IPlaywright>(UniqueId("pw-extract"));
        var result = await grain.ExtractDataAsync("https://example.com", "document.title", ct);
        // returns "not connected" when MCP is unavailable, or actual data when connected
        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    [Fact]
    public async Task GetHistory_AfterScrape_ContainsMessages()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IPlaywright>(UniqueId("pw-hist"));
        await grain.ScrapePageAsync("https://example.com", "get the title", ct);
        var history = await ((IAgent)grain).GetHistory(ct);
        Assert.NotEmpty(history);
    }
}
