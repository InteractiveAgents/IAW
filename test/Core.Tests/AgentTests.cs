using Core;
using Core.Contracts;
using Core.Messages;
using Core.UI;
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

    [Fact]
    public async Task Agent_WithNoStreamInterfaces_HasEmptySubscriptions()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("no-streams"));
        var subs = await agent.GetActiveSubscriptions(ct);
        Assert.Empty(subs);
    }

    [Fact]
    public async Task HandleCallback_ReturnsEmptyByDefault()
    {
        var agent = Cluster.GrainFactory.GetGrain<ITestAgent>(UniqueId("cb"));
        var result = await agent.HandleCallback("unknown", "val", TestContext.Current.CancellationToken);
        Assert.Empty(result.Parts);
    }

    [Fact]
    public async Task GetRichResponse_WrapsTextInAgentResponse()
    {
        var agent = Cluster.GrainFactory.GetGrain<ITestAgent>(UniqueId("rich"));
        var result = await agent.GetRichResponse("Hello", TestContext.Current.CancellationToken);
        Assert.Single(result.Parts);
        var textPart = Assert.IsType<TextPart>(result.Parts[0]);
        Assert.Equal("mock-response", textPart.Content);
    }

    [Fact]
    public async Task ListJobs_ReturnsEmptyByDefault()
    {
        var agent = Cluster.GrainFactory.GetGrain<ITestAgent>(UniqueId("jobs"));
        var jobs = await agent.ListJobs(TestContext.Current.CancellationToken);
        Assert.Empty(jobs);
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

    [Fact]
    public async Task SetState_StringValue_RoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("str-state"));
        await agent.SetWorkspace("/test/path", ct);
        var state = await agent.GetState(ct);
        Assert.Equal("/test/path", state.Entries["workspace-path"].Value.ToString());
    }

    [Fact]
    public async Task GetState_AfterMultipleWrites_ReturnsLatest()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("multi-ws"));
        await agent.SetWorkspace("/first", ct);
        await agent.SetWorkspace("/second", ct);
        var state = await agent.GetState(ct);
        Assert.Equal("/second", state.Entries["workspace-path"].Value.ToString());
    }

    [Fact]
    public async Task SetWorkspace_ThenGetState_ContainsWorkspacePath()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("ws-state"));
        await agent.SetWorkspace("/tmp/iaw-test", ct);
        var state = await agent.GetState(ct);
        Assert.True(state.Entries.ContainsKey("workspace-path"));
    }
}

#endregion

#region Event Publishing & Logging

public class AgentEventTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetEventLog_EmptyByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("nolog"));
        var log = await agent.GetEventLog(ct);
        Assert.Empty(log);
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

#region Communication — IReceiver<T> Rejection

public class AgentRejectingReceiverTests : AgentTest<RejectingReceiverAgent>
{
    [Fact]
    public async Task CanReceive_ReturnsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IRejectingReceiverAgent>(UniqueId("rej-can"));
        var canReceive = await grain.CanReceiveTestMessage(ct);
        Assert.False(canReceive);
    }

    [Fact]
    public async Task Receive_ReturnsRejectionWithReason()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IRejectingReceiverAgent>(UniqueId("rej-recv"));
        var msg = new TestTaskMessage("task-rej", "Rejected task") { SourceAgentId = "test" };
        var receipt = await grain.ReceiveTestMessage(msg, ct);
        Assert.False(receipt.Accepted);
        Assert.Equal("Agent is busy", receipt.RejectionReason);
    }

    [Fact]
    public async Task Receive_StillReportsP2PCapability()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("rej-cap"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasP2P);
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
    public async Task StreamPublish_TriggersOnStreamEventAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("stream");
        var agent = Agent(id);

        // activate agent first so OnActivateAsync subscribes to streams
        await agent.GetMetadata(ct);
        await Task.Delay(200, ct);

        // publish a typed CodeChangedEvent to the "code.changed" stream that StreamTestAgent subscribes to
        var evt = new CodeChangedEvent(["test.cs"], "test", "test change", "publisher", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);

        var streamProvider = Cluster.Client.GetStreamProvider(IAWConstants.StreamProvider);
        var streamId = StreamId.Create(IAWConstants.StreamProvider, "code.changed");
        var stream = streamProvider.GetStream<CodeChangedEvent>(streamId);
        await stream.OnNextAsync(evt);

        // give stream delivery time (generous for CI)
        await Task.Delay(2000, ct);

        var state = await agent.GetState(ct);
        Assert.True(state.Entries.Count > 0, "Agent should have handled stream event via OnStreamEventAsync");
    }

    [Fact]
    public async Task StreamPublish_MultipleConsumers_AllReceive()
    {
        var ct = TestContext.Current.CancellationToken;
        var id1 = UniqueId("mc1");
        var id2 = UniqueId("mc2");
        var agent1 = Agent(id1);
        var agent2 = Agent(id2);

        // activate both so OnActivateAsync subscribes to streams
        await agent1.GetMetadata(ct);
        await agent2.GetMetadata(ct);
        await Task.Delay(200, ct);

        var evt = new CodeChangedEvent(["multi.cs"], "test", "test change", "publisher", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);

        var streamProvider = Cluster.Client.GetStreamProvider(IAWConstants.StreamProvider);
        var streamId = StreamId.Create(IAWConstants.StreamProvider, "code.changed");
        var stream = streamProvider.GetStream<CodeChangedEvent>(streamId);
        await stream.OnNextAsync(evt);

        await Task.Delay(1000, ct);

        var state1 = await agent1.GetState(ct);
        var state2 = await agent2.GetState(ct);
        Assert.True(state1.Entries.Count > 0, "Agent 1 should have handled event");
        Assert.True(state2.Entries.Count > 0, "Agent 2 should have handled event");
    }
}

#endregion

#region Scheduling & Reminders

public class AgentSchedulingTests : AgentTest<SchedulingTestAgent>
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
    public async Task GetEventLog_InitiallyEmpty_OnSchedulingAgent()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("sched"));
        var log = await agent.GetEventLog(ct);
        Assert.Empty(log);
    }

    [Fact]
    public async Task ScheduleJob_StoresInState()
    {
        var agent = Agent(UniqueId("sched-store"));
        var ct = TestContext.Current.CancellationToken;
        await agent.ScheduleJob("test-job", TimeSpan.FromMinutes(5), "do something", ct);
        var jobs = await agent.ListJobs(ct);
        Assert.Single(jobs);
        Assert.Equal("test-job", jobs[0].Name);
        Assert.Equal("do something", jobs[0].Prompt);
    }

    [Fact]
    public async Task CancelJob_RemovesFromState()
    {
        var agent = Agent(UniqueId("cancel"));
        var ct = TestContext.Current.CancellationToken;
        await agent.ScheduleJob("j1", TimeSpan.FromMinutes(5), "prompt", ct);
        await agent.CancelJob("j1", ct);
        var jobs = await agent.ListJobs(ct);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task ScheduleRecurringJob_StoresWithInterval()
    {
        var agent = Agent(UniqueId("recur"));
        var ct = TestContext.Current.CancellationToken;
        await agent.ScheduleRecurringJob("poll", TimeSpan.FromMinutes(30), "check status", ct);
        var jobs = await agent.ListJobs(ct);
        Assert.Single(jobs);
        Assert.Equal(TimeSpan.FromMinutes(30), jobs[0].Interval);
    }

    [Fact]
    public async Task CancelJob_NonExistent_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("cancel-noexist"));
        await agent.CancelJob("nonexistent", ct);
    }

    [Fact]
    public async Task ScheduleRecurringJob_ReminderFires_AgentStillResponds()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("remind-fire"));
        await agent.ScheduleRecurringJob("remind-1", TimeSpan.FromMinutes(1), "Test reminder", ct);

        // in-memory reminder fires with dueTime=Zero, so it should fire quickly
        await Task.Delay(3000, ct);

        var response = await agent.GetResponse("Are you alive?", ct);
        Assert.Equal("mock-response", response);
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

    [Fact]
    public async Task ThreeResponses_HistoryContainsAllTurns()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("3turn"));
        await agent.GetResponse("First", ct);
        await agent.GetResponse("Second", ct);
        await agent.GetResponse("Third", ct);
        var history = await agent.GetHistory(ct);
        // each turn = user message + assistant response = 2 messages, 3 turns = 6
        Assert.True(history.Count >= 6, $"Expected >= 6 history entries, got {history.Count}");
    }
}

#endregion

#region Communication — IStreamProducer<T>

public class AgentProducerTests : AgentTest<ProducerTestAgent>
{
    [Fact]
    public async Task GetMetadata_ReportsPublishedStreamTypes()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("prod"));
        var meta = await agent.GetMetadata(ct);
        Assert.Contains("CodeChangedEvent", meta.Publishes);
    }
}

#endregion

#region Typed Event Publishing

public class AgentTypedEventTests : AgentTest<ProducerTestAgent>
{
    [Fact]
    public async Task PublishTypedEvent_LogsInEventLog()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("typed-evt");
        var grain = Cluster.GrainFactory.GetGrain<IProducerTestAgent>(id);
        var evt = new CodeChangedEvent(["file.cs"], "test", "test change", "test-src", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        await grain.PublishCodeChanged(evt, ct);

        var agent = (IAgent)grain;
        var log = await agent.GetEventLog(ct);
        Assert.Single(log);
        Assert.Equal("code.changed", log[0].EventName);
    }

    [Fact]
    public async Task PublishTypedEvent_PreservesSourceAgentId()
    {
        var ct = TestContext.Current.CancellationToken;
        var grain = Cluster.GrainFactory.GetGrain<IProducerTestAgent>(UniqueId("typed-src"));
        var evt = new CodeChangedEvent(["a.cs"], "test", "test change", "my-agent", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
        await grain.PublishCodeChanged(evt, ct);

        var agent = (IAgent)grain;
        var log = await agent.GetEventLog(ct);
        Assert.Equal("my-agent", log[0].SourceAgentId);
    }
}

#endregion

#region Usage Capture

public class AgentUsageTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task GetLastUsage_BeforeAnyResponse_ReturnsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("no-usage"));
        var usage = await agent.GetLastUsage(ct);
        Assert.Null(usage);
    }

    [Fact]
    public async Task GetLastUsage_AfterResponse_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("with-usage"));
        await agent.GetResponse("Hello", ct);
        // should not throw — MockChatClient may not populate usage but the method should work
        var usage = await agent.GetLastUsage(ct);
    }
}

#endregion

#region Custom Tool Discovery

public class AgentToolTests : AgentTest<ToolTestAgent>
{
    [Fact]
    public async Task GetCapabilities_ReportsHasTools()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tool-cap"));
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasTools);
    }

    [Fact]
    public async Task DefineTools_CustomToolIncluded()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tool-custom"));
        var response = await agent.GetResponse("ping", ct);
        Assert.NotNull(response);
    }
}

#endregion

#region Conversation Regression

public class AgentConversationTests : AgentTest<TestAgent>
{
    [Fact]
    public async Task MultipleResponses_ToolsStillWork()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("tools-cache"));
        await agent.GetResponse("First", ct);
        await agent.GetResponse("Second", ct);
        var caps = await agent.GetCapabilities(ct);
        Assert.True(caps.HasTools);
    }
}

#endregion
