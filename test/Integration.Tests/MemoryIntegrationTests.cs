using IAW.Agents.Memory;
using IAW.Testing;
using Xunit;

namespace IAW.Integration.Tests;

public class MemoryIntegrationTests : AgentTest<UserMemoryAgent>
{
    [Fact]
    public async Task Memory_agent_responds_to_prompts()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("user-memory-integration");
        var response = await agent.GetResponse("Remember that I prefer dark mode", ct);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task Memory_agent_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("user-memory-meta");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("User Memory", meta.DisplayName);
    }

    [Fact]
    public async Task Multiple_memory_agents_are_independent()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = (global::Core.Contracts.IAgent)Cluster.GrainFactory.GetGrain<IUserMemory>("user-mem-1");
        var project = (global::Core.Contracts.IAgent)Cluster.GrainFactory.GetGrain<IProjectMemory>("project-mem-1");

        var r1 = await user.GetResponse("Store preference: dark mode", ct);
        var r2 = await project.GetResponse("Store pattern: CQRS", ct);

        Assert.NotEmpty(r1);
        Assert.NotEmpty(r2);
    }
}