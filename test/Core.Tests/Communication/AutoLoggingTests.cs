using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests.Communication;

public class AutoLoggingTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetResponse_auto_logs_LlmCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("autolog"));
        await agent.GetResponse("hello", ct);
        var log = await agent.GetEventLog(ct);
        Assert.Contains(log, e => e.EventName == "LlmCall");
    }

    [Fact]
    public async Task GetResponse_LlmCall_event_has_prompt_length()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("autolog-pl"));
        await agent.GetResponse("hello", ct);
        var log = await agent.GetEventLog(ct);
        var entry = log.Single(e => e.EventName == "LlmCall");
        Assert.True(entry.Payload.ContainsKey("prompt_length"));
        Assert.Equal(5, (int)entry.Payload["prompt_length"]);
    }

    [Fact]
    public async Task GetResponse_LlmCall_event_has_agent_id_as_source()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("autolog-src");
        var agent = Agent(id);
        await agent.GetResponse("hi", ct);
        var log = await agent.GetEventLog(ct);
        var entry = log.Single(e => e.EventName == "LlmCall");
        Assert.Equal(id, entry.SourceAgentId);
    }

    [Fact]
    public async Task GetResponseStream_auto_logs_LlmStreamCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("autolog-stream"));
        await foreach (var _ in agent.GetResponseStream("hello", ct)) { }
        var log = await agent.GetEventLog(ct);
        Assert.Contains(log, e => e.EventName == "LlmStreamCall");
    }

    [Fact]
    public async Task MultipleGetResponse_calls_log_multiple_LlmCall_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("autolog-multi"));
        await agent.GetResponse("first", ct);
        await agent.GetResponse("second", ct);
        var log = await agent.GetEventLog(ct);
        Assert.Equal(2, log.Count(e => e.EventName == "LlmCall"));
    }
}
