using Core.Contracts;
using Core.Contracts.Events;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class EventRouterTests : AgentTest<TestAgent>
{
    private IEventRouter Router() => Cluster.GrainFactory.GetGrain<IEventRouter>("global");

    [Fact]
    public async Task Route_BuildFailed_WithCS0246_RoutesToFileSystem()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await Router().RouteAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildFailed, "CS0246: type or namespace 'ThemeToggle' not found", null,
            DateTimeOffset.UtcNow), ct);

        Assert.NotNull(result);
        Assert.Equal("filesystem", result!.TargetAgentType);
        Assert.Equal("fix", result.Action);
    }

    [Fact]
    public async Task Route_BuildFailed_WithoutSpecificCode_RoutesToRoslyn()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await Router().RouteAsync(new TaskEvent(
            "DotNet", AgentEventType.BuildFailed, "generic build error", null,
            DateTimeOffset.UtcNow), ct);

        Assert.NotNull(result);
        Assert.Equal("roslyn", result!.TargetAgentType);
        Assert.Equal("analyze", result.Action);
    }

    [Fact]
    public async Task Route_TestFailed_RoutesToDotNet()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await Router().RouteAsync(new TaskEvent(
            "DotNet", AgentEventType.TestFailed, "3 tests failed", null,
            DateTimeOffset.UtcNow), ct);

        Assert.NotNull(result);
        Assert.Equal("dotnet", result!.TargetAgentType);
        Assert.Equal("diagnose", result.Action);
    }

    [Fact]
    public async Task Route_HealthCritical_EscalatesToThread()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await Router().RouteAsync(new TaskEvent(
            "Aspire", AgentEventType.HealthCritical, "p99 latency 2340ms", null,
            DateTimeOffset.UtcNow), ct);

        Assert.NotNull(result);
        Assert.Equal("thread", result!.TargetAgentType);
        Assert.Equal("escalate", result.Action);
    }

    [Fact]
    public async Task Route_InfoEvent_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var result = await Router().RouteAsync(new TaskEvent(
            "Git", AgentEventType.CommitCreated, "abc1234", null,
            DateTimeOffset.UtcNow), ct);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRules_ReturnsAllRules()
    {
        var ct = TestContext.Current.CancellationToken;
        var rules = await Router().GetRulesAsync(ct);
        Assert.True(rules.Count >= 7);
    }
}
