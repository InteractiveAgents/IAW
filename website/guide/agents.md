# Building Agents

This guide covers the three-tier agent hierarchy, constructor parameters, override points, custom tools, behavior interfaces, and discovery.

## Agent Hierarchy

IAW provides a three-tier class hierarchy. All agents ultimately extend `Agent`, which is an Orleans `DurableGrain`:

```
DurableGrain (Orleans.Journaling)
  +-- Agent (IAW.Core) [abstract]
        5 constructor params: state, eventLog, chatClient, history, trackingItems
        |
        +-- LLM (IAW.Core) [abstract]
        |     Same 5 params, default Instructions template
        |     11 concrete agents (one per model)
        |
        +-- Memory (IAW.Core) [abstract]
        |     7 params (+memories, +embedder)
        |     5 concrete agents
        |
        +-- (your custom agents extending Agent directly)
```

### Agent Base Class

Every agent extends `Agent` and implements a grain interface derived from `IAgent`:

```csharp
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public interface IMinimalAgent : IAgent;

public class MinimalAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IMinimalAgent
{
    protected override string Instructions => "You are a minimal agent.";
}
```

This gives you a fully functional agent with durable conversation history, state management, event publishing, tracking, and built-in tools.

### LLM Abstract Class

`LLM` extends `Agent` with a default `Instructions` template. It is the base for model-specific agents that expose a particular LLM as an Orleans grain:

```csharp
public abstract class LLM(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems)
{
    protected override string Instructions =>
        $"You are {DisplayName}. Answer directly and accurately.";
}
```

11 concrete LLM agents ship out of the box, each binding a specific model via `[Llm<TModel>]`:

| Agent | Model | Interface |
|---|---|---|
| `Sonnet46Agent` | Sonnet 4.6 | `ISonnet46` |
| `Opus46Agent` | Opus 4.6 | `IOpus46` |
| `Claude45HaikuAgent` | Claude 4.5 Haiku | `IClaude45Haiku` |
| `Gpt4oAgent` | GPT-4o | `IGpt4o` |
| `Gpt4oMiniAgent` | GPT-4o Mini | `IGpt4oMini` |
| `Gpt52Agent` | GPT-5.2 | `IGpt52` |
| `Gpt53Agent` | GPT-5.3 | `IGpt53` |
| `Gemini31Agent` | Gemini 3.1 | `IGemini31` |
| `GrokLatestAgent` | Grok Latest | `IGrokLatest` |
| `Llama32Agent` | Llama 3.2 | `ILlama32` |
| `Qwen25Agent` | Qwen 2.5 | `IQwen25` |

Example LLM agent (all follow this pattern):

```csharp
public class Sonnet46Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Sonnet46>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : global::IAW.Core.LLM(state, eventLog, chatClient, history, trackingItems), ISonnet46
{
}
```

### Memory Abstract Class

`Memory` extends `Agent` with two additional constructor parameters (`memories` and `embedder`) and provides built-in methods for semantic memory operations:

```csharp
public abstract class Memory(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : Agent(state, eventLog, chatClient, history, trackingItems)
{
    protected abstract string CollectionName { get; }

    protected virtual Task Observe(string content, MemoryProvenance provenance, CancellationToken ct);
    protected virtual Task<IReadOnlyList<MemoryEntry>> Search(string query, int topK = 5, CancellationToken ct);
    protected virtual Task Consolidate(CancellationToken ct);
    protected virtual Task Decay(float decayFactor = 0.95f, CancellationToken ct);
    protected virtual Task Forget(string memoryId, CancellationToken ct);
}
```

5 concrete Memory agents ship out of the box:

| Agent | Collection | Interface | Purpose |
|---|---|---|---|
| `UserMemoryAgent` | `iaw-user-memory` | `IUserMemory` | User preferences, personal facts |
| `ProjectMemoryAgent` | `iaw-project-memory` | `IProjectMemory` | Project-level knowledge |
| `PatternMemoryAgent` | `iaw-pattern-memory` | `IPatternMemory` | Recurring patterns and conventions |
| `EpisodeMemoryAgent` | `iaw-episode-memory` | `IEpisodeMemory` | Task execution episodes |
| `CodeMemoryAgent` | `iaw-code-memory` | `ICodeMemory` | Code structure and relationships |

Example Memory agent:

```csharp
public class UserMemoryAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::IAW.Core.Memory(state, eventLog, chatClient, history, trackingItems, memories, embedder),
      IUserMemory
{
    protected override string CollectionName => "iaw-user-memory";
    protected override string DisplayName => "User Memory";
    protected override string Instructions =>
        "You manage user preferences, personal facts, and corrections. " +
        "Extract and remember personal information from conversations.";
}
```

## CodeOrchestrator Agent

`CodeOrchestratorAgent` (implements `ICodeOrchestrator`) handles complex multi-step tasks by generating, compiling, and executing standalone C# scripts that coordinate other agents.

### How It Works

1. Receives a task description (via `CreateTask`)
2. Uses the LLM to decompose the task into an `OrchestrationPlan` (ordered steps with agent assignments)
3. `ScriptGenerator` turns the plan into a C# file using `InterfaceCatalog` to resolve grain interfaces and IDs
4. `OrchestrationCompiler` validates the script with Roslyn (compile-only, no execution)
5. The script is written to the workspace folder and executed out-of-process via `dotnet script` or `dotnet run`
6. The script connects to the Orleans cluster as a client and invokes agents directly
7. Progress, completion, and failure events flow back through the task stream

### Workspace Structure

The workspace is a configurable disk folder set via `.WithWorkspace(path)` in the Aspire AppHost:

```
workspace/
  scripts/
    task-abc123.csx          # generated orchestration script
    task-abc123.log           # stdout/stderr capture
  artifacts/
    ...                       # any files produced by the task
```

Scripts are retained after execution for debugging and audit. Each task gets a unique script file named by its `TaskId`.

### Interface

```csharp
public interface ICodeOrchestrator : IAgent
{
    Task<string> CreateTask(string description, CancellationToken ct = default);
    Task<TaskState> GetTaskState(string taskId, CancellationToken ct = default);
    Task PauseTask(string taskId, CancellationToken ct = default);
    Task ResumeTask(string taskId, CancellationToken ct = default);
}
```

### Position in the Agent Hierarchy

`CodeOrchestrator` extends `Agent` directly (not `LLM` or `Memory`). It sits alongside `PersonalAssistant` at the top of the orchestration layer:

```
PersonalAssistant
  |-- (Mode 1) delegates directly to sub-agents via GetResponse
  |-- (Mode 2) delegates to CodeOrchestrator for complex tasks
        |-- generates scripts that invoke any agent in the cluster
```

`PersonalAssistant` selects between Mode 1 (LLM delegation) and Mode 2 (code orchestration) based on task complexity. See [Architecture: Dual Orchestration Modes](/guide/architecture#dual-orchestration-modes) for details.

### Tool Result Handling

When sub-agents return results during orchestration, the raw output is passed through Claude 4.5 Haiku for summarization before entering the orchestrator's conversation history. This prevents large outputs (build logs, code listings, search results) from consuming disproportionate context in subsequent LLM calls.

## Constructor Parameters

The five base `Agent` constructor parameters are injected by Orleans:

| Parameter | Type | Purpose |
|---|---|---|
| `state` | `IDurableDictionary<string, StateEntry>` | Key-value state store (workspace path, custom data) |
| `eventLog` | `IDurableList<AgentEvent>` | Append-only event log |
| `chatClient` | `IChatClient` | LLM provider from Microsoft.Extensions.AI |
| `history` | `IDurableList<ChatMessage>` | Conversation history |
| `trackingItems` | `IDurableDictionary<string, TrackingItem>` | Scheduled tracking items |

`Memory` agents add two more:

| Parameter | Type | Purpose |
|---|---|---|
| `memories` | `IDurableList<MemoryEntry>` | Durable memory store with relevance scores |
| `embedder` | `IEmbeddingGenerator<string, Embedding<float>>` | Embedding generator for semantic search |

::: tip
You never instantiate these yourself. Orleans resolves the `[Memory]`-annotated parameters from journaled grain storage and the `IChatClient` from dependency injection. The `[Llm<TModel>]` attribute resolves to a keyed `IChatClient` for a specific model.
:::

## Override Points

`Agent` exposes four virtual members:

| Member | Default | Purpose |
|---|---|---|
| `Instructions` | `"You are a helpful AI assistant..."` | LLM system prompt |
| `DisplayName` | `GetType().Name` | Human-readable name for metadata |
| `DefineTools()` | Empty list | Custom AI tools for the LLM |
| `OnTrackingDueAsync()` | LLM-powered check | Handle tracking item due events |

### Instructions

The system prompt sent to the LLM on every conversation turn:

```csharp
protected override string Instructions =>
    "You are a code review expert. Analyze code for bugs, security issues, and style.";
```

### DisplayName

Used in metadata and the agent registry:

```csharp
protected override string DisplayName => "Code Review Bot";
```

### DefineTools

Override to add custom tools the LLM can call. Use `AIFunctionFactory.Create()`:

```csharp
using System.ComponentModel;
using Microsoft.Extensions.AI;

protected override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create(SearchKnowledgeBase),
    AIFunctionFactory.Create(CreateReminder)
];

[Description("Search the knowledge base for relevant information")]
private async Task<string> SearchKnowledgeBase(
    [Description("Search query")] string query)
{
    return $"Results for: {query}";
}

[Description("Create a reminder for a future date")]
private async Task<string> CreateReminder(
    [Description("Reminder text")] string text,
    [Description("Due date")] DateTime dueDate)
{
    State[$"reminder-{Guid.NewGuid():N}"] = new StateEntry("reminder", text);
    await WriteStateAsync(AgentCancellation);
    return $"Reminder set for {dueDate:g}";
}
```

::: warning
Tool methods must have a `[Description]` attribute. Without it, the method will not be discovered by the tool registration system.
:::

## Conversation

The agent provides two conversation methods:

```csharp
// Single response
var response = await agent.GetResponse("What's the weather?", ct);

// Streaming response
await foreach (var chunk in agent.GetResponseStream("Tell me a story", ct))
{
    Console.Write(chunk);
}
```

Conversation history is persisted in the durable `history` list via `DurableChatHistoryProvider`. Clear it with:

```csharp
await agent.ClearHistory(ct);
```

## State Management

The agent's state is a durable dictionary of `StateEntry` records:

```csharp
// Set workspace (enables FileTools and ShellTools)
await agent.SetWorkspace("/path/to/project", ct);

// Read all state
var state = await agent.GetState(ct);
foreach (var entry in state.Entries)
{
    Console.WriteLine($"{entry.Key} = {entry.Value.Value}");
}
```

Inside the agent class, access state directly:

```csharp
State["my-key"] = new StateEntry("my-key", "my-value");
await WriteStateAsync(AgentCancellation);
```

## Events

Publish events to the event log and Orleans streams:

```csharp
// Typed event (preferred)
await PublishToStream(new CodeChangedEvent(
    SourceAgentId: this.GetPrimaryKeyString(),
    CorrelationId: Guid.NewGuid().ToString(),
    Timestamp: DateTimeOffset.UtcNow,
    FilePaths: ["src/Agent.cs"],
    CommitSha: "abc123"), ct);

// Task-scoped event
await PublishToTaskStream(taskId, new StepProgressEvent(
    SourceAgentId: this.GetPrimaryKeyString(),
    CorrelationId: correlationId,
    Timestamp: DateTimeOffset.UtcNow,
    TaskId: taskId,
    StepDescription: "Running build..."), ct);
```

See [Events & Streams](/guide/events-streams) for detailed patterns.

## Behavior Interfaces

Add communication capabilities by implementing typed interfaces:

```csharp
public class MyAgent : Agent,
    IStreamConsumer<CodeChangedEvent>,    // auto-subscribes to code.changed stream
    IStreamProducer<BuildCompletedEvent>, // can publish build.completed events
    IReceiver<AssignTaskCommand>,         // can receive directed commands
    IBroadcaster<AlertNotification>       // can broadcast to registered receivers
{
    public async Task OnStreamEventAsync(CodeChangedEvent evt, StreamSequenceToken? token)
    {
        var review = await GetResponse($"Review: {string.Join(", ", evt.FilePaths)}", AgentCancellation);
    }

    public async Task<MessageReceipt> ReceiveAsync(AssignTaskCommand cmd, CancellationToken ct)
    {
        await GetResponse($"Task: {cmd.Description}", ct);
        return new MessageReceipt(true, this.GetPrimaryKeyString(), DateTimeOffset.UtcNow, null);
    }

    public Task<bool> CanReceiveAsync(CancellationToken ct) => Task.FromResult(true);
}
```

## InterfaceCatalog

`InterfaceCatalog` discovers all `IAgent`-derived interfaces at runtime and builds a catalog of agent capabilities. It scans loaded assemblies for grain interfaces and their concrete implementations:

```csharp
var catalog = InterfaceCatalog.Discover();
foreach (var entry in catalog)
{
    Console.WriteLine($"{entry.InterfaceName} (id: {entry.GrainId})");
    Console.WriteLine($"  Produces: {string.Join(", ", entry.Produces)}");
    Console.WriteLine($"  Consumes: {string.Join(", ", entry.Consumes)}");
    Console.WriteLine($"  Receives: {string.Join(", ", entry.Receives)}");
}
```

Each `CatalogEntry` contains:

| Property | Type | Source |
|---|---|---|
| `InterfaceName` | `string` | The grain interface name (e.g., `ISonnet46`) |
| `GrainId` | `string` | Computed grain ID (e.g., `sonnet46`) |
| `InterfaceType` | `Type` | The .NET type for the interface |
| `Produces` | `IReadOnlyList<string>` | Event types from `IStreamProducer<T>` |
| `Consumes` | `IReadOnlyList<string>` | Event types from `IStreamConsumer<T>` |
| `Receives` | `IReadOnlyList<string>` | Message types from `IReceiver<T>` |

The catalog is used by `ScriptGenerator` to resolve agent interfaces when generating orchestration scripts, and by `PersonalAssistantAgent` to understand what agents are available.

Call `InterfaceCatalog.ToPromptString(entries)` to render the catalog as a markdown string suitable for injecting into LLM prompts.

## Metadata and Capabilities

The agent automatically reports its metadata based on implemented interfaces and attributes:

```csharp
var metadata = await agent.GetMetadata(ct);
// metadata.AgentType = "MyAgent"
// metadata.DisplayName = "My Agent"

var caps = await agent.GetCapabilities(ct);
// caps.HasMemory = true
// caps.HasEvents = true
// caps.HasTools = true
```

## Cancellation

Every agent has a cancellation token accessible via `AgentCancellation`. Cancel an agent externally:

```csharp
await agent.Cancel(ct);
```

This cancels the current token and creates a new one, stopping any in-progress LLM calls or tool executions.

## Complete Example: Weather Agent

```csharp
using System.ComponentModel;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public interface IWeatherAgent : IAgent;

public class WeatherAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IWeatherAgent
{
    protected override string Instructions =>
        "You're a weather assistant. Use the available tools to answer questions about weather.";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(GetCurrentWeather),
        AIFunctionFactory.Create(GetForecast)
    ];

    [Description("Gets the current weather for a given city")]
    static WeatherInfo GetCurrentWeather(string city) => new(
        City: city,
        TemperatureCelsius: Random.Shared.Next(-10, 40),
        Condition: PickRandom("Sunny", "Cloudy", "Rainy", "Snowy"),
        Humidity: Random.Shared.Next(20, 100));

    [Description("Gets a 3-day weather forecast for a given city")]
    static List<ForecastDay> GetForecast(string city) =>
    [.. Enumerable.Range(1, 3)
        .Select(i => new ForecastDay(
            Date: DateOnly.FromDateTime(DateTime.Now.AddDays(i)),
            HighCelsius: Random.Shared.Next(15, 40),
            LowCelsius: Random.Shared.Next(-5, 15),
            Condition: PickRandom("Sunny", "Cloudy", "Rainy")))];

    static string PickRandom(params string[] options) =>
        options[Random.Shared.Next(options.Length)];
}

public record WeatherInfo(string City, int TemperatureCelsius, string Condition, int Humidity);
public record ForecastDay(DateOnly Date, int HighCelsius, int LowCelsius, string Condition);
```
