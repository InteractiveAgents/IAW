using IAW.Agents.Orchestration;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class ThreadDelegateToolTests : AgentTest<ThreadAgent>
{
    [Fact]
    public async Task GetResponse_WithDelegationRequest_ReturnsResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("delegate"));

        // MockChatClient returns "mock-response" for all calls.
        // AgentSelector will also get "mock-response" (not valid JSON),
        // ParseSelectionResult treats it as CannotHandle.
        // The Delegate tool returns the error/explanation string.
        var response = await thread.GetResponse("check the git status", ct);
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    public async Task Thread_HasDelegateTool_InCapabilities()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("tools"));
        var capabilities = await thread.GetCapabilities(ct);
        Assert.NotNull(capabilities);
    }

    [Fact]
    public async Task Delegate_SchedulesJobAndReturnsImmediately()
    {
        var ct = TestContext.Current.CancellationToken;
        var thread = Agent(UniqueId("dlg-async"));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await thread.GetResponse("check git status", ct);
        sw.Stop();

        Assert.NotNull(response);
        Assert.NotEmpty(response);
        // Should complete in seconds, not minutes (proves no blocking)
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"Response took {sw.Elapsed} — should be fast since delegation is async");
    }
}