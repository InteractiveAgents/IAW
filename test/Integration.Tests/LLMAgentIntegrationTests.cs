using Core.AI.Models;
using Core.Contracts;
using Core.Orchestration;
using IAW.Agents.Models;
using IAW.Testing;
using Xunit;

namespace IAW.Integration.Tests;

public class LLMAgentIntegrationTests : AgentTest<Opus46Agent>
{
    [Fact]
    public void InterfaceCatalog_discovers_all_LLM_agents()
    {
        var catalog = InterfaceCatalog.Discover();
        var llmInterfaces = new[] { "IOpus46", "ISonnet46", "IClaude45Haiku", "IGpt4o", "IGpt4oMini", "IGpt52", "IGpt53", "IGemini31", "IGrokLatest", "ILlama32", "IQwen25" };

        foreach (var name in llmInterfaces)
            Assert.Contains(catalog, e => e.InterfaceName == name);
    }

    [Fact]
    public async Task LLM_agent_responds_to_prompt()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("opus46-test");
        var response = await agent.GetResponse("What is 2+2?", ct);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task LLM_agent_metadata_reflects_model_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("opus46-metadata");
        var meta = await agent.GetMetadata(ct);
        Assert.Contains("Opus 4.6", meta.DisplayName);
    }

    [Fact]
    public async Task Multiple_LLM_agents_work_independently()
    {
        var ct = TestContext.Current.CancellationToken;
        var opus = (IAgent)Cluster.GrainFactory.GetGrain<IOpus46>("opus-multi");
        var sonnet = (IAgent)Cluster.GrainFactory.GetGrain<ISonnet46>("sonnet-multi");

        var r1 = await opus.GetResponse("Hello from Opus", ct);
        var r2 = await sonnet.GetResponse("Hello from Sonnet", ct);

        Assert.NotEmpty(r1);
        Assert.NotEmpty(r2);
    }
}
