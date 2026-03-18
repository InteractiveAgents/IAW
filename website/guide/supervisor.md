# Task Supervision

IAW includes a supervision system for monitoring task health, detecting stalls, and escalating problems. The `TaskSupervisor` tracks registered tasks, the `NotificationAgent` routes alerts to the appropriate channels, and the `PersonalAssistantAgent` coordinates the overall flow. This page covers task registration, health monitoring, stall detection, and notification routing.

## TaskSupervisor

The `TaskSupervisor` is a grain that tracks active tasks, collects progress reports from agents, and detects when tasks stall. It works alongside the `PersonalAssistantAgent`, which maintains task state in its durable dictionary.

### Registering a Task

When the `PersonalAssistantAgent` assigns a task via `AssignTaskToAgent`, it records the task in its state:

```csharp
var taskId = Guid.NewGuid().ToString("N")[..8];
State[$"task-{taskId}"] = new StateDescriptor($"task-{taskId}",
    JsonSerializer.Serialize(new
    {
        Description = description,
        AssignedTo = agentKey,
        Status = "assigned"
    }));
await WriteStateAsync(ct);

await PublishAsync("task.assigned", new Dictionary<string, object>
{
    ["TaskId"] = taskId,
    ["AssignedTo"] = agentKey,
    ["Description"] = description
}, ct);
```

A supervisor agent can subscribe to these events to begin monitoring:

```csharp
public interface ITaskSupervisor : IAgent
{
    Task RegisterTask(string taskId, string assignedTo,
        string description, CancellationToken ct = default);
    Task ReportProgress(string taskId, string status,
        float progress, CancellationToken ct = default);
    Task<TaskHealthRecord> GetTaskHealth(string taskId,
        CancellationToken ct = default);
    Task<IReadOnlyList<TaskHealthRecord>> GetAllTaskHealth(
        CancellationToken ct = default);
}
```

### Implementing the TaskSupervisor

```csharp
public class TaskSupervisorAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient),
      ITaskSupervisor,
      IStreamConsumer<TaskCompletedMessage>,
      IStreamConsumer<TaskFailedMessage>
{
    protected override string DisplayName => "Task Supervisor";
    protected override string SystemPrompt =>
        "You monitor task health and detect stalled or failing tasks.";

    public async Task RegisterTask(
        string taskId, string assignedTo, string description,
        CancellationToken ct)
    {
        var record = new TaskHealthRecord(
            TaskId: taskId,
            AssignedTo: assignedTo,
            Description: description,
            Status: "assigned",
            Progress: 0f,
            RegisteredAt: DateTimeOffset.UtcNow,
            LastProgressAt: DateTimeOffset.UtcNow,
            ProgressUpdates: 0,
            IsStalled: false,
            StallReason: null);

        State[$"health-{taskId}"] = new StateDescriptor(
            $"health-{taskId}", JsonSerializer.Serialize(record));
        await WriteStateAsync(ct);
    }

    public async Task ReportProgress(
        string taskId, string status, float progress, CancellationToken ct)
    {
        if (!State.TryGetValue($"health-{taskId}", out var desc))
            return;

        var record = JsonSerializer.Deserialize<TaskHealthRecord>(
            desc.Value.ToString()!);
        if (record is null) return;

        var updated = record with
        {
            Status = status,
            Progress = progress,
            LastProgressAt = DateTimeOffset.UtcNow,
            ProgressUpdates = record.ProgressUpdates + 1,
            IsStalled = false,
            StallReason = null
        };

        State[$"health-{taskId}"] = new StateDescriptor(
            $"health-{taskId}", JsonSerializer.Serialize(updated));
        await WriteStateAsync(ct);
    }

    public Task<TaskHealthRecord> GetTaskHealth(string taskId, CancellationToken ct)
    {
        if (!State.TryGetValue($"health-{taskId}", out var desc))
            return Task.FromResult<TaskHealthRecord>(null!);

        var record = JsonSerializer.Deserialize<TaskHealthRecord>(
            desc.Value.ToString()!);
        return Task.FromResult(record!);
    }

    public Task<IReadOnlyList<TaskHealthRecord>> GetAllTaskHealth(CancellationToken ct)
    {
        var records = State
            .Where(kvp => kvp.Key.StartsWith("health-"))
            .Select(kvp => JsonSerializer.Deserialize<TaskHealthRecord>(
                kvp.Value.Value.ToString()!)!)
            .Where(r => r is not null)
            .ToList();

        return Task.FromResult<IReadOnlyList<TaskHealthRecord>>(records);
    }
}
```

## TaskHealthRecord

The health record tracks everything about a task's lifecycle:

```csharp
[GenerateSerializer]
public record TaskHealthRecord(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string AssignedTo,
    [property: Id(2)] string Description,
    [property: Id(3)] string Status,
    [property: Id(4)] float Progress,
    [property: Id(5)] DateTimeOffset RegisteredAt,
    [property: Id(6)] DateTimeOffset LastProgressAt,
    [property: Id(7)] int ProgressUpdates,
    [property: Id(8)] bool IsStalled,
    [property: Id(9)] string? StallReason);
```

| Field | Purpose |
|---|---|
| `TaskId` | Unique task identifier |
| `AssignedTo` | Grain key of the assigned agent |
| `Description` | What the task should accomplish |
| `Status` | Current status: `assigned`, `in-progress`, `completed`, `failed`, `stalled` |
| `Progress` | 0.0 to 1.0 completion percentage |
| `RegisteredAt` | When the task was created |
| `LastProgressAt` | Last time progress was reported |
| `ProgressUpdates` | Total number of progress reports received |
| `IsStalled` | Whether the supervisor has flagged this task |
| `StallReason` | Why the task is considered stalled |

## Stall Detection

Stall detection uses Orleans tracking items (reminders) to periodically check task health. When a task has not reported progress within a configurable threshold, the supervisor marks it as stalled and escalates.

### Implementing Stall Detection

```csharp
// In TaskSupervisorAgent
protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(CheckForStalledTasks)
];

[Description("Check all active tasks for stalls and escalate if needed")]
private async Task<string> CheckForStalledTasks(
    [Description("Minutes without progress before stall")] int stallThresholdMinutes = 10)
{
    var threshold = TimeSpan.FromMinutes(stallThresholdMinutes);
    var now = DateTimeOffset.UtcNow;
    var stalledTasks = new List<TaskHealthRecord>();

    foreach (var kvp in State.Where(k => k.Key.StartsWith("health-")))
    {
        var record = JsonSerializer.Deserialize<TaskHealthRecord>(
            kvp.Value.Value.ToString()!);
        if (record is null) continue;

        // Skip completed or already-stalled tasks
        if (record.Status is "completed" or "failed") continue;

        var timeSinceProgress = now - record.LastProgressAt;
        if (timeSinceProgress > threshold)
        {
            var stalled = record with
            {
                IsStalled = true,
                Status = "stalled",
                StallReason = $"No progress for {timeSinceProgress.TotalMinutes:F0} minutes"
            };

            State[kvp.Key] = new StateDescriptor(
                kvp.Key, JsonSerializer.Serialize(stalled));
            stalledTasks.Add(stalled);
        }
    }

    if (stalledTasks.Count == 0)
        return "All tasks are healthy.";

    await WriteStateAsync(AgentCancellation);

    // Escalate stalled tasks
    foreach (var task in stalledTasks)
    {
        await EscalateStall(task);
    }

    return $"Found {stalledTasks.Count} stalled task(s). Escalated to notifications.";
}

private async Task EscalateStall(TaskHealthRecord task)
{
    var notification = GrainFactory.GetGrain<INotification>("notification");
    await notification.HandleEvent(new AgentEvent(
        "task.stalled",
        this.GetPrimaryKeyString(),
        Guid.NewGuid().ToString(),
        DateTimeOffset.UtcNow,
        new Dictionary<string, object>
        {
            ["TaskId"] = task.TaskId,
            ["AssignedTo"] = task.AssignedTo,
            ["Description"] = task.Description,
            ["StallReason"] = task.StallReason ?? "Unknown"
        }), AgentCancellation);
}
```

### Periodic Health Checks with Tracking Items

Use tracking items to schedule periodic stall checks:

```csharp
// Register a periodic health check when the supervisor activates
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);

    // Schedule health check every 5 minutes
    if (!TrackingItems.ContainsKey("health-check"))
    {
        TrackingItems["health-check"] = new TrackingItem(
            "health-check",
            "Periodic task health check",
            Interval: TimeSpan.FromMinutes(5),
            CreatedAt: DateTimeOffset.UtcNow,
            LastCheckAt: null,
            LastResult: null);
        await WriteStateAsync(ct);
    }
}
```

## NotificationAgent

The `NotificationAgent` aggregates events and delivers notifications. It receives events from other agents and routes them to the appropriate channel.

```csharp
public class NotificationAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(state, eventLog, trackingItems, chatClient), INotification, IEventDrivenAgent
{
    protected override string DisplayName => "Notification Hub";
    protected override string SystemPrompt =>
        "Aggregates events and delivers notifications";

    public override async Task HandleEvent(
        AgentEvent agentEvent, CancellationToken ct)
    {
        await PublishAsync("notification.delivered", new Dictionary<string, object>
        {
            ["OriginalEvent"] = agentEvent.EventName,
            ["Source"] = agentEvent.SourceAgentId,
            ["Payload"] = agentEvent.Payload,
            ["DeliveredAt"] = DateTimeOffset.UtcNow
        }, ct);
    }
}
```

### NotificationChannel

Route notifications to different delivery channels based on severity and type:

```csharp
public enum NotificationChannel
{
    Dashboard,   // Aspire dashboard via structured logs
    Telegram,    // Telegram bot integration
    Email,       // Email notifications
    Log          // Standard logging (default)
}
```

### Channel Routing

```csharp
[Description("Route a notification to the appropriate channel")]
private async Task<string> RouteNotification(
    [Description("Event name")] string eventName,
    [Description("Severity: info, warning, critical")] string severity,
    [Description("Message body")] string message)
{
    var channel = severity switch
    {
        "critical" => NotificationChannel.Telegram,
        "warning" => NotificationChannel.Dashboard,
        _ => NotificationChannel.Log
    };

    switch (channel)
    {
        case NotificationChannel.Dashboard:
            await PublishAsync("notification.dashboard", new Dictionary<string, object>
            {
                ["Event"] = eventName,
                ["Severity"] = severity,
                ["Message"] = message
            });
            return $"Notification sent to Dashboard: {message}";

        case NotificationChannel.Telegram:
            var telegramAgent = GrainFactory.GetGrain<IAgent>("telegram-bot");
            await telegramAgent.SendMessage(
                new ChatMessage($"[{severity.ToUpperInvariant()}] {eventName}: {message}",
                    ChatRole.User), AgentCancellation)
                .LastAsync(AgentCancellation);
            return $"Notification sent to Telegram: {message}";

        case NotificationChannel.Email:
            // Email delivery via external service
            State[$"email-{Guid.NewGuid():N}"[..12]] = new StateDescriptor("email",
                JsonSerializer.Serialize(new { eventName, severity, message }));
            await WriteStateAsync();
            return $"Email queued: {message}";

        default:
            await PublishAsync("notification.log", new Dictionary<string, object>
            {
                ["Event"] = eventName,
                ["Message"] = message
            });
            return $"Logged: {message}";
    }
}
```

## Escalation Flow

The complete escalation flow from stall detection to notification delivery:

```
1. PersonalAssistant assigns task to Worker agent
   --> State["task-{id}"] = { Status: "assigned", AssignedTo: "worker-1" }
   --> PublishAsync("task.assigned", ...)

2. TaskSupervisor registers the task
   --> State["health-{id}"] = TaskHealthRecord { Status: "assigned" }

3. Worker reports progress periodically
   --> supervisor.ReportProgress(taskId, "building", 0.5f)

4. Progress stops (agent stalls, LLM timeout, etc.)
   --> Supervisor's periodic check detects no progress for 10 minutes

5. Supervisor marks task as stalled
   --> TaskHealthRecord { IsStalled: true, StallReason: "No progress for 10 minutes" }

6. Supervisor escalates to NotificationAgent
   --> notification.HandleEvent("task.stalled", { TaskId, AssignedTo, StallReason })

7. NotificationAgent routes based on severity
   --> Critical: Telegram message to the user
   --> Warning: Dashboard notification
   --> Info: Structured log entry

8. PersonalAssistant can query stalled tasks
   --> var health = await supervisor.GetAllTaskHealth(ct);
   --> Retry, reassign, or cancel stalled tasks
```

## Task Lifecycle Events

The supervision system uses these events to track task lifecycle:

| Event | Published By | Meaning |
|---|---|---|
| `task.assigned` | PersonalAssistant | New task created and delegated |
| `task.completed` | Worker agent | Task finished successfully |
| `task.failed` | Worker agent | Task encountered an error |
| `task.stalled` | TaskSupervisor | No progress within threshold |
| `notification.delivered` | NotificationAgent | Alert sent to a channel |

### Receiving Completion Messages

The `PersonalAssistantAgent` receives task outcomes via P2P messaging:

```csharp
public class PersonalAssistantAgent : Agent,
    IReceiver<TaskCompletedMessage>,
    IReceiver<TaskFailedMessage>
{
    public async Task<MessageReceipt> Receive(
        TaskCompletedMessage message, CancellationToken ct)
    {
        State[$"completed-{message.TaskId}"] = new StateDescriptor(
            $"completed-{message.TaskId}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        await PublishAsync("task.completed", new Dictionary<string, object>
        {
            ["TaskId"] = message.TaskId,
            ["CompletedBy"] = message.CompletedBy,
            ["Result"] = message.Result
        }, ct);

        return new MessageReceipt(true,
            Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
    }

    public async Task<MessageReceipt> Receive(
        TaskFailedMessage message, CancellationToken ct)
    {
        State[$"failed-{message.TaskId}"] = new StateDescriptor(
            $"failed-{message.TaskId}",
            JsonSerializer.Serialize(message));
        await WriteStateAsync(ct);

        await PublishAsync("task.failed", new Dictionary<string, object>
        {
            ["TaskId"] = message.TaskId,
            ["FailedBy"] = message.FailedBy,
            ["Error"] = message.Error
        }, ct);

        return new MessageReceipt(true,
            Guid.NewGuid().ToString(), DateTimeOffset.UtcNow);
    }
}
```

## Querying Active Tasks

The `PersonalAssistantAgent` exposes active task queries through its interface:

```csharp
public interface IPersonalAssistant : IAgent
{
    Task<string> GetTeamStatusAsync(CancellationToken ct = default);
    Task<string[]> GetActiveTasksAsync(CancellationToken ct = default);
}
```

```csharp
var pa = GrainFactory.GetGrain<IPersonalAssistant>("personal-assistant");
var tasks = await pa.GetActiveTasksAsync(ct);
var status = await pa.GetTeamStatusAsync(ct);
```

The team status tool queries the agent registry and checks the state of all known agents, providing a comprehensive view of the system's health.
