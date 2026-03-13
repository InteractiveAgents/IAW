using Core.Communication;
using Core.Contracts;
using ChatMessage = Core.Contracts.ChatMessage;
using Core.Messages;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using Orleans.Streams;

namespace IAW.Core.Tests;

// basic test agent — no communication interfaces
public interface ITestAgent : IAgent;

public class TestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), ITestAgent
{
    protected override string Instructions => "You are a test agent.";
    protected override string DisplayName => "Test Agent";
}

// test agent with IReceiver<TestTaskMessage> for P2P communication tests
public interface IReceiverTestAgent : IAgent
{
    Task<MessageReceipt> ReceiveTestMessage(TestTaskMessage message, CancellationToken ct = default);
    Task<bool> CanReceiveTestMessage(CancellationToken ct = default);
}

[GenerateSerializer]
public record TestTaskMessage(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Description) : IAgentMessage
{
    [Id(2)] public string SourceAgentId { get; init; } = string.Empty;
    [Id(3)] public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
    [Id(4)] public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public class ReceiverTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient),
      IReceiverTestAgent,
      IReceiver<TestTaskMessage>
{
    protected override string Instructions => "Receiver test agent.";
    protected override string DisplayName => "Receiver Test";

    public readonly List<TestTaskMessage> ReceivedMessages = [];

    public Task<MessageReceipt> ReceiveAsync(TestTaskMessage message, CancellationToken ct = default)
        => ReceiveTestMessage(message, ct);

    public Task<bool> CanReceiveAsync(CancellationToken ct = default)
        => CanReceiveTestMessage(ct);

    public Task<MessageReceipt> ReceiveTestMessage(TestTaskMessage message, CancellationToken ct = default)
    {
        ReceivedMessages.Add(message);
        State[$"received-{message.TaskId}"] = new StateEntry($"received-{message.TaskId}", message.Description);
        return Task.FromResult(new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, null));
    }

    public Task<bool> CanReceiveTestMessage(CancellationToken ct = default) => Task.FromResult(true);
}

// test agent with IStreamConsumer<CodeChangedEvent> for stream subscription tests
public interface IStreamTestAgent : IAgent;

public class StreamTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient),
      IStreamTestAgent,
      IStreamConsumer<CodeChangedEvent>
{
    protected override string Instructions => "Stream test agent.";
    protected override string DisplayName => "Stream Test";

    public int EventsHandled { get; private set; }

    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        EventsHandled++;
        State[$"handled-{EventsHandled}"] = new StateEntry($"handled-{EventsHandled}", string.Join(",", evt.FilePaths));
        await WriteStateAsync(default);
    }
}

// test agent that overrides OnTrackingDueAsync for tracking tests
public interface ITrackingTestAgent : IAgent
{
    Task StartTestTracking(string name, string description, TimeSpan interval, CancellationToken ct = default);
    Task StopTestTracking(string name, CancellationToken ct = default);
}

public class TrackingTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), ITrackingTestAgent
{
    protected override string Instructions => "Tracking test agent.";
    protected override string DisplayName => "Tracking Test";

    public int TrackingCheckCount { get; private set; }
    public TrackingItem? LastCheckedItem { get; private set; }

    public async Task StartTestTracking(string name, string description, TimeSpan interval, CancellationToken ct = default)
    {
        var item = new TrackingItem(name, description, interval, DateTimeOffset.UtcNow, null, null);
        await StartTrackingAsync(name, item, interval, ct);
    }

    public async Task StopTestTracking(string name, CancellationToken ct = default)
    {
        await StopTrackingAsync(name, ct);
    }

    protected override Task OnTrackingDueAsync(TrackingItem item, CancellationToken ct)
    {
        TrackingCheckCount++;
        LastCheckedItem = item;
        TrackingItems[item.Id] = item with { LastResult = $"check-{TrackingCheckCount}" };
        return Task.CompletedTask;
    }
}

// test agent with IStreamProducer<CodeChangedEvent> for publish discovery tests
public interface IProducerTestAgent : IAgent
{
    Task PublishCodeChanged(CodeChangedEvent evt, CancellationToken ct = default);
}

public class ProducerTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient),
      IProducerTestAgent,
      IStreamProducer<CodeChangedEvent>
{
    protected override string Instructions => "Producer test agent.";
    protected override string DisplayName => "Producer Test";

    public async Task PublishToStreamAsync(CodeChangedEvent evt, CancellationToken ct = default)
        => await PublishToStream(evt, ct);

    public async Task PublishCodeChanged(CodeChangedEvent evt, CancellationToken ct = default)
        => await PublishToStreamAsync(evt, ct);
}

// test agent that rejects P2P messages with a reason
public interface IRejectingReceiverAgent : IAgent
{
    Task<MessageReceipt> ReceiveTestMessage(TestTaskMessage message, CancellationToken ct = default);
    Task<bool> CanReceiveTestMessage(CancellationToken ct = default);
}

public class RejectingReceiverAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient),
      IRejectingReceiverAgent,
      IReceiver<TestTaskMessage>
{
    protected override string Instructions => "Rejecting receiver.";
    protected override string DisplayName => "Rejecting Receiver";

    public Task<MessageReceipt> ReceiveAsync(TestTaskMessage message, CancellationToken ct = default)
        => ReceiveTestMessage(message, ct);

    public Task<bool> CanReceiveAsync(CancellationToken ct = default)
        => CanReceiveTestMessage(ct);

    public Task<MessageReceipt> ReceiveTestMessage(TestTaskMessage message, CancellationToken ct = default)
        => Task.FromResult(new MessageReceipt(false, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, "Agent is busy"));

    public Task<bool> CanReceiveTestMessage(CancellationToken ct = default) => Task.FromResult(false);
}

// test agent with custom DefineTools for tool discovery tests
public interface IToolTestAgent : IAgent;

public class ToolTestAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient), IToolTestAgent
{
    protected override string Instructions => "Tool test agent.";
    protected override string DisplayName => "Tool Test";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(() => "pong", "Ping", "Returns pong")
    ];
}
