using Core.Contracts;
using IAW.Agents.Memory;
using IAW.Agents.Personal;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ExplainabilityAgentTests : AgentTest<ExplainabilityAgent>
{
    [Fact]
    public async Task Explain_WithNoMemories_ReturnsGracefully()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = (IExplainability)Agent(UniqueId("explain-empty"));

        var result = await agent.ExplainAsync("Why did you use Cosmos DB?", ct);

        Assert.NotNull(result);
        Assert.Equal("Why did you use Cosmos DB?", result.Question);
        Assert.Contains("couldn't find", result.Explanation.ToLowerInvariant());
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task Explain_WithPreferences_FindsRelevant()
    {
        var ct = TestContext.Current.CancellationToken;

        var prefAgent = Cluster.GrainFactory.GetGrain<IPreference>("preferences");
        await prefAgent.SetRuleAsync(PreferenceRule.Create(
            "architecture", "Use Cosmos for sub-10ms reads",
            "User said latency matters more than cost on March 15", "high"), ct);

        var agent = (IExplainability)Agent(UniqueId("explain-pref"));
        var traces = await agent.SearchAllMemoriesAsync("Cosmos", ct: ct);

        Assert.NotEmpty(traces);
        Assert.Contains(traces, t => t.Content.Contains("Cosmos"));
    }

    [Fact]
    public async Task Explain_WithPreferences_SynthesizesAnswer()
    {
        var ct = TestContext.Current.CancellationToken;

        var prefAgent = Cluster.GrainFactory.GetGrain<IPreference>("preferences");
        await prefAgent.SetRuleAsync(PreferenceRule.Create(
            "testing", "No mocks in integration tests",
            "Past incident: mock/prod divergence", "high"), ct);

        var agent = (IExplainability)Agent(UniqueId("explain-synth"));
        var result = await agent.ExplainAsync("mocks", ct);

        Assert.NotNull(result);
        Assert.NotEmpty(result.Sources);
        Assert.NotNull(result.Explanation);
    }

    [Fact]
    public async Task Explain_WithKnowledgeDecision_FindsRelevant()
    {
        var ct = TestContext.Current.CancellationToken;

        var knowledge = Cluster.GrainFactory.GetGrain<IKnowledge>("knowledge");
        await knowledge.AddDecision(
            "Use Orleans for agent framework",
            "Need virtual actor model for scalable stateful agents",
            "Adopted Orleans 10.x with journaling");

        var agent = (IExplainability)Agent(UniqueId("explain-decision"));
        var traces = await agent.SearchAllMemoriesAsync("Orleans", ct: ct);

        Assert.NotEmpty(traces);
        Assert.Contains(traces, t => t.MemoryType == "Decision" && t.Content.Contains("Orleans"));
    }

    [Fact]
    public async Task SearchAllMemories_WithNoData_ReturnsEmptyList()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = (IExplainability)Agent(UniqueId("search-all"));

        var traces = await agent.SearchAllMemoriesAsync("anything", ct: ct);

        Assert.NotNull(traces);
        Assert.Empty(traces);
    }
}
