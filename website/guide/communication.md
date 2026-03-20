# Communication

IAW agents communicate through three distinct channels: typed pub/sub streams, point-to-point messaging, and broadcast fan-out. Each channel serves a different coordination pattern. This page covers the APIs, when to use each, and how to combine them.

## Channel Overview

| Channel | Interface | Direction | Delivery | Use Case |
|---|---|---|---|---|
| Typed Pub/Sub | `IStreamConsumer<T>` / `IStreamProducer<T>` | One-to-many | Fire-and-forget via Orleans streams | Event pipelines, decoupled reactions |
| Point-to-Point | `IReceiver<T>` | One-to-one | Request-reply with `MessageReceipt` | Task assignment, directed commands |
| Broadcast | `IBroadcaster<T>` | One-to-many | Fan-out with delivery tracking | Coordinated multi-agent work |

## Three-Mechanism Comparison

The framework provides three distinct mechanisms for agent-to-agent and agent-to-external communication. They differ in coupling, delivery semantics, and who initiates the connection.

| | `IReceiver<T>` | Streams (`IStreamConsumer<T>`) | Observers (`IGrainObserver`) |
|---|---|---|---|
| **Direction** | Sender → specific receiver | Publisher → any subscriber | Grain → subscribed watchers |
| **Coupling** | Tight — sender knows receiver | Loose — publisher doesn't know subscribers | Medium — grain holds observer refs |
| **Typing** | Strongly typed messages | Strongly typed events | Custom observer interface |
| **Who initiates** | Sender pushes | Subscriber subscribes to stream | Watcher subscribes to grain |
| **Delivery** | Synchronous, awaitable | Async, fire-and-forget | Direct callback, in-memory |
| **Persistence** | No | Depends on stream provider | No |
| **Use case** | "Hey DotNet, code changed — react" | "Code changed event happened, anyone who cares" | "Telegram client watching for real-time updates" |

### How They Work Together

A typical workflow combines all three mechanisms. A Git agent detects a commit, directly notifies the DotNet agent via `IReceiver<T>`, the DotNet agent publishes a build result to a stream, and a Telegram client receives real-time updates via an observer.

```
Git detects commit
  --> GitAgent calls DotNetAgent.Receive(CodeChangedCommand)   // IReceiver<T>: tight, directed
      --> DotNetAgent builds and publishes BuildCompletedEvent  // Stream: loose, fan-out
          --> Any subscriber (ReviewAgent, CIAgent) reacts
          --> TelegramClient observes DotNetAgent directly      // Observer: real-time push to external watcher
```

In code:

```csharp
// 1. Git → DotNet via IReceiver<T> (sender knows the target)
var dotnet = GrainFactory.GetGrain<IDotNetAgent>("dotnet");
var receipt = await dotnet.Receive(new CodeChangedCommand(commitSha, changedFiles), ct);

// 2. DotNet publishes build result to stream (no knowledge of consumers)
public class DotNetAgent : Agent, IStreamProducer<BuildCompletedEvent>
{
    public async Task PublishToStreamAsync(BuildCompletedEvent evt, CancellationToken ct)
        => await PublishTypedAsync(evt, ct);
}

// 3. Telegram client observes DotNet grain for real-time updates (external watcher)
var observer = await observerFactory.CreateObjectReference<IBuildObserver>(telegramNotifier);
await dotnet.SubscribeObserverAsync(observer);
```

## Typed Pub/Sub Streams

Typed pub/sub uses Orleans streams under the `"agents"` provider. Agents publish typed events to a stream and other agents subscribe by implementing `IStreamConsumer<T>`. Subscriptions are auto-wired on grain activation.

### Publishing Events

Any agent can publish events using `PublishAsync` (untyped) or `PublishTypedAsync` (typed). Both record the event in the durable event log and broadcast it on the corresponding Orleans stream.

```csharp
// Untyped -- publish with a string name and payload dictionary
await PublishAsync("build.completed", new Dictionary<string, object>
{
    ["Success"] = true,
    ["CommitSha"] = "abc123"
}, ct);

// Typed -- publish via IStreamProducer<T>
public class BuildAgent : Agent, IStreamProducer<BuildCompletedEvent>
{
    public async Task PublishToStreamAsync(BuildCompletedEvent evt, CancellationToken ct)
    {
        await PublishTypedAsync(evt, ct);
    }
}
```

The `IStreamProducer<T>` interface declares the intent to publish a specific event type. It requires implementing `PublishToStreamAsync`, which typically delegates to the base class `PublishTypedAsync`.

### Consuming Events

Implement `IStreamConsumer<T>` to auto-subscribe to an Orleans stream on activation. The stream name is derived from the type name.

```csharp
using IAW.Core.Communication;
using Orleans.Streams;

public class ReviewAgent : Agent, IStreamConsumer<CodeChangedEvent>
{
    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        var files = string.Join(", ", evt.FilePaths);
        await GetResponse($"Review changes in: {files}", AgentCancellation);
    }
}
```

The base class scans for `IStreamConsumer<T>` interfaces during `OnActivateAsync` and subscribes to the corresponding stream. You do not need to manage subscriptions manually.

### Stream Name Resolution

Type names are converted to stream names by stripping the suffix (`Event`, `Command`, `Notification`) and converting PascalCase to dot.case:

| Type Name | Stream Name |
|---|---|
| `CodeChangedEvent` | `code.changed` |
| `BuildCompletedEvent` | `build.completed` |
| `TestsPassedEvent` | `tests.passed` |
| `AssignTaskCommand` | `assign.task` |

This conversion is handled by `Agent.EventTypeToStreamName()`.

## Point-to-Point Messaging

Point-to-point (P2P) messaging sends a typed message to a specific agent and receives an acknowledgment. The sender gets a `MessageReceipt` confirming delivery.

### IReceiver&lt;T&gt;

The receiver implements `IReceiver<T>` with two methods:

```csharp
using IAW.Core.Communication;

public class WorkerAgent : Agent, IReceiver<AssignTaskCommand>
{
    public async Task<MessageReceipt> Receive(AssignTaskCommand cmd, CancellationToken ct)
    {
        var result = await GetResponse($"Execute: {cmd.Description}", ct);
        return new MessageReceipt(true, this.GetPrimaryKeyString(), DateTimeOffset.UtcNow);
    }

    public Task<bool> CanReceive(CancellationToken ct) => Task.FromResult(true);
}
```

`CanReceive` lets the receiver signal whether it is currently able to accept messages. Senders can check this before dispatching.

### MessageReceipt

Every P2P delivery returns a `MessageReceipt`:

```csharp
[GenerateSerializer]
public record MessageReceipt(
    [property: Id(0)] bool Accepted,
    [property: Id(1)] string ReceiptId,
    [property: Id(2)] DateTimeOffset Timestamp,
    [property: Id(3)] string? RejectionReason = null);
```

If `Accepted` is false, `RejectionReason` explains why. This lets callers implement retry or fallback logic.

### Sending a P2P Message

The sender resolves the receiver grain and calls `Receive` directly:

```csharp
var worker = GrainFactory.GetGrain<IAgent>("worker-1") as IReceiver<AssignTaskCommand>;
var receipt = await worker.Receive(new AssignTaskCommand(
    "coordinator", Guid.NewGuid().ToString(), DateTimeOffset.UtcNow,
    "Run integration tests", "/src/project"), ct);

if (!receipt.Accepted)
    logger.LogWarning("Task rejected: {Reason}", receipt.RejectionReason);
```

### Conversational P2P

For conversational interaction (sending a prompt and streaming the response), use the `IAgent` interface directly:

```csharp
var agent = GrainFactory.GetGrain<IAgent>("roslyn");
var message = new ChatMessage("Analyze the architecture of this project", ChatRole.User);

await foreach (var response in agent.SendMessage(message, ct))
{
    if (response.Kind == AgentResponseKind.Text)
        Console.Write(response.Content);
}
```

This is the pattern used by `PersonalAssistantAgent.AssignTaskToAgent` to delegate work to team members.

## Broadcast Fan-Out

Broadcast sends a single message to multiple registered receivers with delivery tracking.

### IBroadcaster&lt;T&gt;

```csharp
using IAW.Core.Communication;

public class CoordinatorAgent : Agent, IBroadcaster<AssignTaskCommand>
{
    private readonly HashSet<string> _receivers = [];

    public async Task<BroadcastResult> BroadcastAsync(
        AssignTaskCommand message, CancellationToken ct)
    {
        var delivered = 0;
        var failed = new List<string>();

        foreach (var id in _receivers)
        {
            try
            {
                var agent = GrainFactory.GetGrain<IAgent>(id);
                await agent.SendMessage(
                    new ChatMessage($"Task: {message.Description}", ChatRole.User), ct)
                    .LastAsync(ct);
                delivered++;
            }
            catch { failed.Add(id); }
        }

        return new BroadcastResult(
            _receivers.Count, delivered, failed.Count, [.. failed]);
    }

    public Task RegisterReceiverAsync(string receiverId)
    {
        _receivers.Add(receiverId);
        return Task.CompletedTask;
    }

    public Task UnregisterReceiverAsync(string receiverId)
    {
        _receivers.Remove(receiverId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> GetReceiversAsync()
        => Task.FromResult<IReadOnlyList<string>>([.. _receivers]);
}
```

### BroadcastResult

```csharp
[GenerateSerializer]
public record BroadcastResult(
    [property: Id(0)] int TotalReceivers,
    [property: Id(1)] int Delivered,
    [property: Id(2)] int Failed,
    [property: Id(3)] IReadOnlyList<string> FailedReceiverIds);
```

The result tells you exactly how many receivers got the message and which ones failed, enabling retry logic for partial failures.

## Observer Notifications

For push notifications to external subscribers (not other grains), use `INotifier<T>`:

```csharp
using IAW.Core.Communication;

public class MonitorAgent : Agent, INotifier<AlertNotification>
{
    public async Task NotifyAsync(AlertNotification notification, CancellationToken ct)
    {
        // Push to all subscribed observers
    }

    public Task SubscribeObserverAsync(IAgentObserver<AlertNotification> observer) { ... }
    public Task UnsubscribeObserverAsync(IAgentObserver<AlertNotification> observer) { ... }
}
```

Observers implement `IAgentObserver<T>` and receive callbacks outside the Orleans grain context -- typically from client applications or UI dashboards.

## When to Use Which

| Scenario | Channel | Why |
|---|---|---|
| Code changed, trigger CI | Typed Pub/Sub | Decoupled -- publisher does not know consumers |
| Assign a specific task to a specific agent | P2P (IReceiver) | Directed delivery with acknowledgment |
| Distribute work to a pool of workers | Broadcast | Fan-out with delivery tracking |
| Notify a dashboard of an alert | Observer (INotifier) | Push to external subscribers |
| Agent-to-agent conversation | Conversational P2P | Streaming response via SendMessage |

## Combined Flow Example

A typical development workflow combines all three channels:

```
1. Developer pushes code
   --> CodeChangedEvent published to "code.changed" stream (Pub/Sub)

2. SelfImprovementAgent receives the event (IStreamConsumer<CodeChangedEvent>)
   --> Records the code change in its state

3. PersonalAssistant assigns a review task to Reviewer (P2P)
   --> AssignTaskToAgent("reviewer", "Review the latest changes")
   --> Gets back a MessageReceipt

4. Reviewer completes the review
   --> Sends ReviewCompletedMessage to PersonalAssistant (P2P via IReceiver)

5. PersonalAssistant broadcasts the result to all interested agents
   --> BroadcastAsync to registered receivers

6. NotificationAgent receives the broadcast
   --> Pushes an alert to the dashboard (Observer)
```

In code, the `PersonalAssistantAgent` orchestrates this flow:

```csharp
public class PersonalAssistantAgent : Agent,
    IReceiver<TaskCompletedMessage>,
    IReceiver<TaskFailedMessage>,
    IReceiver<ReviewCompletedMessage>,
    IReceiver<DeploySucceededMessage>
{
    // P2P: receive task completion from workers
    public async Task<MessageReceipt> Receive(
        TaskCompletedMessage message, CancellationToken ct)
    {
        State[$"completed-{message.TaskId}"] = new StateDescriptor(
            $"completed-{message.TaskId}", JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        // Pub/Sub: broadcast completion event
        await PublishAsync("task.completed", new Dictionary<string, object>
        {
            ["TaskId"] = message.TaskId,
            ["CompletedBy"] = message.CompletedBy
        }, ct);

        return new MessageReceipt(true, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
    }
}
```

## Declaring Communication in Metadata

Use `[Publishes]` and `[Subscribes]` attributes to declare an agent's communication contracts at the class level:

```csharp
[Publishes("review.completed")]
[Subscribes("code.changed")]
public class CodeReviewAgent : Agent { ... }
```

These attributes are picked up by `AgentRegistrationStartupTask` and stored in the agent registry, making it possible to query for agents by their communication patterns:

```csharp
var registry = GrainFactory.GetGrain<IAgentRegistryGrain>("global");
var reviewers = await registry.QueryAsync(new AgentQuery(
    Subscribes: ["code.changed"]));
```
