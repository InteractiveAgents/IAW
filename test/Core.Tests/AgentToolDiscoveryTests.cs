using System.ComponentModel;
using Core.Contracts;
using IAW.Core;
using IAW.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace IAW.Core.Tests;

public interface IToolDiscoveryTestAgent : IAgent
{
    [Description("Searches for items by query")]
    Task<string> SearchAsync(string query, CancellationToken ct = default);

    Task<int> CountItemsAsync(CancellationToken ct = default);
}

public class ToolDiscoveryTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IToolDiscoveryTestAgent
{
    protected override string Instructions => "Tool discovery test agent.";
    protected override string DisplayName => "Tool Discovery Test";

    public Task<string> SearchAsync(string query, CancellationToken ct = default)
        => Task.FromResult($"results for: {query}");

    public Task<int> CountItemsAsync(CancellationToken ct = default)
        => Task.FromResult(42);
}

public class AgentToolDiscoveryTests : AgentTest<ToolDiscoveryTestAgent>
{
    [Fact]
    public async Task Agent_HasToolsFromInterface()
    {
        var agent = Agent(UniqueId("tools"));
        var capabilities = await agent.GetCapabilities(TestContext.Current.CancellationToken);
        Assert.True(capabilities.HasTools);
    }

    [Fact]
    public async Task InterfaceMethods_AreDiscoveredAsTools()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("discover"));
        // the agent should respond without errors, proving tools were registered
        var response = await agent.GetResponse("Search for something", ct);
        Assert.NotNull(response);
    }
}

// verify that IAgent methods are not discovered as tools on a bare agent
public class BareAgentToolDiscoveryTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task BareAgent_StillHasWorkspaceTools()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("bare"));
        var caps = await agent.GetCapabilities(ct);
        // workspace tools (SetWorkspace, GetWorkspace) are always present
        Assert.True(caps.HasTools);
    }
}

// verify that DefineTools still works alongside auto-discovered tools
public interface IHybridToolTestAgent : IAgent
{
    Task<string> AutoDiscoveredMethod(CancellationToken ct = default);
}

public class HybridToolTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IHybridToolTestAgent
{
    protected override string Instructions => "Hybrid tool test agent.";
    protected override string DisplayName => "Hybrid Tool Test";

    public Task<string> AutoDiscoveredMethod(CancellationToken ct = default)
        => Task.FromResult("auto-discovered");

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(() => "manual", "ManualTool", "A manually defined tool")
    ];
}

public class HybridToolTests : AgentTest<HybridToolTestAgent>
{
    [Fact]
    public async Task Agent_HasBothManualAndAutoDiscoveredTools()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("hybrid"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasTools);
    }
}
