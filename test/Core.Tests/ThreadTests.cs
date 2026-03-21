using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ThreadTests : AgentTest<ThreadAgent>
{
    [Fact]
    public async Task GetResponse_ReturnsResponse()
    {
        var thread = Agent(UniqueId("thread"));
        var response = await thread.GetResponse("hello", TestContext.Current.CancellationToken);
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task HandleCallback_UnknownId_ReturnsEmpty()
    {
        var thread = Agent(UniqueId("cb-thread"));
        var result = await thread.HandleCallback("unknown-id", "value", TestContext.Current.CancellationToken);
        Assert.Empty(result.Parts);
    }

    [Fact]
    public async Task GetMetadata_ReturnsThreadDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("thread-meta"));
        var metadata = await thread.GetMetadata(ct);
        Assert.Equal("Thread", metadata.DisplayName);
    }

    [Fact]
    public async Task GetHistory_TracksConversation()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("thread-hist"));
        await thread.GetResponse("test message", ct);
        var history = await thread.GetHistory(ct);
        Assert.True(history.Count >= 2);
    }

    [Fact]
    public async Task ClearHistory_EmptiesHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("thread-clear"));
        await thread.GetResponse("hello", ct);
        await thread.ClearHistory(ct);
        var history = await thread.GetHistory(ct);
        Assert.Empty(history);
    }

    [Fact]
    public async Task GetHistory_PreservesVerbatimUserMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("verbatim"));

        await thread.GetResponse(@"Create a calculator at D:\IAW\Calc", ct);
        var history = await thread.GetHistory(ct);

        var lastUserMsg = history.LastOrDefault(m => m.Role == "user");
        Assert.NotNull(lastUserMsg);
        Assert.Contains(@"D:\IAW\Calc", lastUserMsg.Text);
    }
}
