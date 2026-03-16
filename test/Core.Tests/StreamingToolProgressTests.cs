using System.ComponentModel;
using Core.Contracts;
using IAW.Testing;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Xunit;

namespace IAW.Core.Tests;

public interface IToolProgressTestAgent : IAgent;

public class ToolProgressTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IToolProgressTestAgent
{
    protected override string Instructions => "You are a test agent. Use the SlowTool when asked.";
    protected override string DisplayName => "ToolProgress Test";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(SlowTool, nameof(SlowTool),
            "A tool that writes progress updates")
    ];

    [Description("A tool that writes progress updates")]
    private Task<string> SlowTool()
    {
        WriteToolProgress("[progress:start]");
        WriteToolProgress("[progress:end]");
        return Task.FromResult("tool-done");
    }
}

public class StreamingToolProgressTests : AgentTest<ToolProgressTestAgent>
{
    [Fact]
    public async Task GetResponseStream_YieldsChunksWithChannelInfrastructure()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("stream"));

        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStream("Hello", ct))
            chunks.Add(chunk);

        var combined = string.Join("", chunks);
        Assert.NotEmpty(chunks);
        Assert.Contains("mock-response", combined);
    }

    [Fact]
    public async Task GetResponse_ReturnsFullTextWithChannelInfrastructure()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("full"));

        var response = await agent.GetResponse("Hello", ct);
        Assert.Equal("mock-response", response);
    }

    [Fact]
    public async Task GetResponseStream_PreservesEventLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("log"));

        await agent.GetResponse("hello", ct);

        var log = await agent.GetEventLog(ct);
        var llmEvent = log.FirstOrDefault(e => e.EventName == "LlmCall");
        Assert.NotNull(llmEvent);
        Assert.Equal(5, (int)llmEvent.Payload["prompt_length"]);
    }
}
