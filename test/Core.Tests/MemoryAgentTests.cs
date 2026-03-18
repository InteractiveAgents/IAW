using IAW.Agents.Memory;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class UserMemoryAgentTests : AgentTest<UserMemoryAgent>
{
    [Fact]
    public async Task UserMemory_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("user-memory");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("User Memory", meta.DisplayName);
    }

    [Fact]
    public async Task UserMemory_responds_to_GetResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("user-memory");
        var response = await agent.GetResponse("hello", ct);
        Assert.NotNull(response);
    }
}

public class ProjectMemoryAgentTests : AgentTest<ProjectMemoryAgent>
{
    [Fact]
    public async Task ProjectMemory_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("project-memory");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("Project Memory", meta.DisplayName);
    }

    [Fact]
    public async Task ProjectMemory_responds_to_GetResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("project-memory");
        var response = await agent.GetResponse("hello", ct);
        Assert.NotNull(response);
    }
}

public class PatternMemoryAgentTests : AgentTest<PatternMemoryAgent>
{
    [Fact]
    public async Task PatternMemory_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("pattern-memory");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("Pattern Memory", meta.DisplayName);
    }

    [Fact]
    public async Task PatternMemory_responds_to_GetResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("pattern-memory");
        var response = await agent.GetResponse("hello", ct);
        Assert.NotNull(response);
    }
}

public class EpisodeMemoryAgentTests : AgentTest<EpisodeMemoryAgent>
{
    [Fact]
    public async Task EpisodeMemory_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("episode-memory");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("Episode Memory", meta.DisplayName);
    }

    [Fact]
    public async Task EpisodeMemory_responds_to_GetResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("episode-memory");
        var response = await agent.GetResponse("hello", ct);
        Assert.NotNull(response);
    }
}

public class CodeMemoryAgentTests : AgentTest<CodeMemoryAgent>
{
    [Fact]
    public async Task CodeMemory_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-memory");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("Code Memory", meta.DisplayName);
    }

    [Fact]
    public async Task CodeMemory_responds_to_GetResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-memory");
        var response = await agent.GetResponse("hello", ct);
        Assert.NotNull(response);
    }
}
