using IAW.Agents.Projects;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ProjectTests : AgentTest<Project>
{
    [Fact]
    public async Task GetResponse_WorksLikeStandardAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project"));
        var response = await project.GetResponse("hello", ct);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetHistory_TracksConversation()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project-hist"));
        await project.GetResponse("test message", ct);
        var history = await project.GetHistory(ct);
        Assert.True(history.Count >= 2);
    }

    [Fact]
    public async Task GetMetadata_ReturnsProjectDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project-meta"));
        var metadata = await project.GetMetadata(ct);
        Assert.Equal("Project", metadata.DisplayName);
    }

    [Fact]
    public async Task ClearHistory_EmptiesHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var project = Agent(UniqueId("project-clear"));
        await project.GetResponse("hello", ct);
        await project.ClearHistory(ct);
        var history = await project.GetHistory(ct);
        Assert.Empty(history);
    }
}
