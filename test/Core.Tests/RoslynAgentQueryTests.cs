using IAW.Agents.Coding;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

public class RoslynAgentQueryTests : AgentTest<RoslynAgent>
{
    [Fact]
    public async Task GetWorkspaceStatus_ReturnsStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var roslyn = Agent(UniqueId("roslyn-status"));
        var status = await ((IRoslyn)roslyn).GetWorkspaceStatusAsync(ct);
        Assert.NotNull(status);
        Assert.Contains("not loaded", status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetCallersOf_WithoutWorkspace_ReturnsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var roslyn = Agent(UniqueId("roslyn-callers"));
        var result = await ((IRoslyn)roslyn).GetCallersOfAsync("SomeMethod", ct);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetCalleesOf_WithoutWorkspace_ReturnsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var roslyn = Agent(UniqueId("roslyn-callees"));
        var result = await ((IRoslyn)roslyn).GetCalleesOfAsync("SomeMethod", ct);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetImplementors_WithoutWorkspace_ReturnsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var roslyn = Agent(UniqueId("roslyn-impl"));
        var result = await ((IRoslyn)roslyn).GetImplementorsAsync("IAgent", ct);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetBaseTypes_WithoutWorkspace_ReturnsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var roslyn = Agent(UniqueId("roslyn-base"));
        var result = await ((IRoslyn)roslyn).GetBaseTypesAsync("RoslynAgent", ct);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetOverrides_WithoutWorkspace_ReturnsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var roslyn = Agent(UniqueId("roslyn-overrides"));
        var result = await ((IRoslyn)roslyn).GetOverridesAsync("OnActivateAsync", ct);
        Assert.NotNull(result);
    }
}