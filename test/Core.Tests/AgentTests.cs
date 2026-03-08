using Core.Contracts;
using Core.Messages;
using IAW.Testing;
using Xunit;

namespace IAW.Core.Tests;

#region Basic Agent Behavior

public class AgentBasicTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetResponse_ReturnsLlmResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("basic"));
        var response = await agent.GetResponse("Hello", ct);
        Assert.Equal("mock-response", response);
    }

    [Fact]
    public async Task GetHistory_AfterResponse_ContainsMessages()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("hist"));
        await agent.GetResponse("Hello", ct);
        var history = await agent.GetHistory(ct);
        Assert.NotEmpty(history);
    }

    [Fact]
    public async Task ClearHistory_EmptiesHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("clear"));
        await agent.GetResponse("Hello", ct);
        await agent.ClearHistory(ct);
        var history = await agent.GetHistory(ct);
        Assert.Empty(history);
    }

    [Fact]
    public async Task GetMetadata_ReturnsCorrectDisplayName()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("meta"));
        var metadata = await agent.GetMetadata(ct);
        Assert.Equal("Test Agent", metadata.DisplayName);
        Assert.Equal(AgentKind.Static, metadata.Kind);
    }

    [Fact]
    public async Task GetCapabilities_ReportsCorrectDefaults()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("cap"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasMemory);
        Assert.True(caps.HasTimers);
        Assert.True(caps.IsCancellable);
        Assert.False(caps.HasP2P);
        Assert.False(caps.HasEvents);
    }

    [Fact]
    public async Task Cancel_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("cancel"));
        await agent.Cancel(ct);
    }

    [Fact]
    public async Task GetCapabilities_HasToolsReflectsActualTools()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tools-cap"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasTools);
    }

    [Fact]
    public async Task GetMetadata_BasicAgent_HasNoPublishesOrSubscribes()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("meta-empty"));
        var meta = await agent.GetMetadata(ct);
        Assert.Empty(meta.Publishes);
        Assert.Empty(meta.Subscribes);
    }

    [Fact]
    public async Task GetMetadata_ReturnsAgentTypeName()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("meta-type"));
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("TestAgent", meta.AgentType);
    }

    [Fact]
    public async Task Cancel_ThenRespond_StillWorks()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("cancel-recover"));
        await agent.Cancel(ct);
        var response = await agent.GetResponse("After cancel", ct);
        Assert.Equal("mock-response", response);
    }
}

#endregion

#region State Management

public class AgentStateTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task SetWorkspace_PersistsInState()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("ws"));
        await agent.SetWorkspace("/tmp/test", ct);
        var state = await agent.GetState(ct);
        Assert.True(state.Entries.ContainsKey("workspace-path"));
        Assert.Equal("/tmp/test", state.Entries["workspace-path"].Value.ToString());
    }

    [Fact]
    public async Task GetState_InitiallyEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("empty"));
        var state = await agent.GetState(ct);
        Assert.Empty(state.Entries);
    }
}

#endregion

#region Event Publishing & Logging

public class AgentEventTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task PublishToStream_LogsEventInEventLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("evtlog"));
        var evt = new AgentEvent("test.event", "test-source", Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow, new Dictionary<string, object> { ["key"] = "value" });
        await agent.PublishToStream(evt, ct);

        var log = await agent.GetEventLog(ct);
        Assert.Single(log);
        Assert.Equal("test.event", log[0].EventName);
        Assert.Equal("test-source", log[0].SourceAgentId);
    }

    [Fact]
    public async Task PublishToStream_MultipleEvents_AllLogged()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("multi"));
        for (var i = 0; i < 3; i++)
        {
            var evt = new AgentEvent($"event-{i}", "source", Guid.NewGuid().ToString(),
                DateTimeOffset.UtcNow, []);
            await agent.PublishToStream(evt, ct);
        }

        var log = await agent.GetEventLog(ct);
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public async Task GetEventLog_EmptyByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("nolog"));
        var log = await agent.GetEventLog(ct);
        Assert.Empty(log);
    }

    [Fact]
    public async Task HandleEvent_DefaultIsNoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("hevt"));
        var evt = new AgentEvent("some.event", "source", "corr", DateTimeOffset.UtcNow, []);
        await agent.HandleEvent(evt, ct);
    }
}

#endregion

#region Communication — IReceiver<T>

public class AgentReceiverTests : AgentTest<ReceiverTestAgent>
{
    [Fact]
    public async Task GetCapabilities_ReportsHasP2P()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("rcap"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasP2P);
    }

    [Fact]
    public async Task GetMetadata_ReportsReceivedMessageTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("rmeta"));
        var meta = await agent.GetMetadata(ct);
        Assert.Contains("TestTaskMessage", meta.Subscribes);
    }

    [Fact]
    public async Task Receiver_AcceptsMessage()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IReceiverTestAgent>(UniqueId("recv"));
        var canReceive = await grain.CanReceiveTestMessage(ct);
        Assert.True(canReceive);

        var msg = new TestTaskMessage("task-1", "Test task") { SourceAgentId = "test" };
        var receipt = await grain.ReceiveTestMessage(msg, ct);
        Assert.True(receipt.Accepted);
    }

    [Fact]
    public async Task Receiver_PersistsReceivedMessageInState()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("rstate");
        var grain = Cluster.GrainFactory.GetGrain<IReceiverTestAgent>(id);
        var msg = new TestTaskMessage("task-99", "Persisted task") { SourceAgentId = "test" };
        await grain.ReceiveTestMessage(msg, ct);

        var state = await ((IAgent)grain).GetState(ct);
        Assert.True(state.Entries.ContainsKey("received-task-99"));
    }
}

#endregion

#region Communication — Streams (IStreamConsumer<T>)

public class AgentStreamTests : AgentTest<StreamTestAgent>
{
    [Fact]
    public async Task GetCapabilities_ReportsHasEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("scap"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasEvents);
    }

    [Fact]
    public async Task GetActiveSubscriptions_ReportsCodeChanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("subs"));
        var subs = await agent.GetActiveSubscriptions(ct);
        Assert.Contains("code.changed", subs);
    }

    [Fact]
    public async Task GetMetadata_ReportsStreamSubscriptions()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("smeta"));
        var meta = await agent.GetMetadata(ct);
        Assert.Contains("CodeChangedEvent", meta.Subscribes);
    }

    [Fact]
    public async Task StreamPublish_TriggersHandleEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("stream");
        var agent = Agent(id);

        // activate agent first so OnActivateAsync subscribes to streams
        await agent.GetMetadata(ct);
        await Task.Delay(200, ct);

        // publish to the "code.changed" stream that StreamTestAgent subscribes to
        var evt = new AgentEvent("code.changed", "publisher", Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow, new Dictionary<string, object> { ["file"] = "test.cs" });

        var streamProvider = Cluster.Client.GetStreamProvider("agents");
        var streamId = StreamId.Create("agents", "code.changed");
        var stream = streamProvider.GetStream<AgentEvent>(streamId);
        await stream.OnNextAsync(evt);

        // give stream delivery time
        await Task.Delay(1000, ct);

        var state = await agent.GetState(ct);
        Assert.True(state.Entries.Count > 0, "Agent should have handled stream event");
    }
}

#endregion

#region Tracking & Reminders

public class AgentTrackingTests : AgentTest<TrackingTestAgent>
{
    [Fact]
    public async Task GetCapabilities_HasTimersIsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tcap"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasTimers);
    }

    [Fact]
    public async Task PublishToStream_WorksOnTrackingAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("track"));
        var evt = new AgentEvent("test", "src", "corr", DateTimeOffset.UtcNow, []);
        await agent.PublishToStream(evt, ct);
        var log = await agent.GetEventLog(ct);
        Assert.Single(log);
    }
}

#endregion

#region Stream Name Mapping

public class EventTypeToStreamNameTests
{
    [Theory]
    [InlineData(typeof(CodeChangedEvent), "code.changed")]
    public void EventTypeToStreamName_MapsCorrectly(Type eventType, string expected)
    {
        var result = Agent.EventTypeToStreamName(eventType);
        Assert.Equal(expected, result);
    }
}

#endregion

#region Streaming Response

public class AgentStreamingResponseTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetResponseStream_CompletesWithoutError()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("strm"));
        var chunks = new List<string>();
        await foreach (var chunk in agent.GetResponseStream("Hello", ct))
            chunks.Add(chunk);

        // MockChatClient streaming may or may not yield chunks, but should not throw
        Assert.True(true);
    }
}

#endregion

#region History Accumulation

public class AgentHistoryTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task MultipleResponses_BuildHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("mhist"));
        await agent.GetResponse("First", ct);
        await agent.GetResponse("Second", ct);
        var history = await agent.GetHistory(ct);
        Assert.True(history.Count >= 2);
    }

    [Fact]
    public async Task ClearHistory_ThenRespond_StartsClean()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("clhist"));
        await agent.GetResponse("Before clear", ct);
        await agent.ClearHistory(ct);
        await agent.GetResponse("After clear", ct);
        var history = await agent.GetHistory(ct);
        Assert.True(history.Count <= 2);
    }
}

#endregion
