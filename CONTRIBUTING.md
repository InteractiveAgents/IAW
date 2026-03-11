# Contributing to IAW

We welcome contributions! Here's how to get started.

## Development Setup

1. Install [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
2. Install [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
3. Clone the repository:
   ```bash
   git clone https://github.com/InteractiveAgents/IAW.git
   cd IAW
   ```
4. Build:
   ```bash
   dotnet build IAW.slnx
   ```
5. Run tests:
   ```bash
   dotnet test IAW.slnx
   ```
6. Run locally:
   ```bash
   aspire run
   ```

## Writing Agents

Agents extend `Agent` (which itself extends Orleans `DurableGrain`) and use primary constructor injection for durable state, LLM client, and tracking. Override `DisplayName` and `Instructions`, and optionally `DefineTools()`:

```csharp
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public class MyAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Sonnet46>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IMyAgent
{
    protected override string DisplayName => "My Agent";
    protected override string Instructions => "You handle specific tasks.";
}
```

The five constructor parameters are:
- `state` -- durable key-value store for agent state
- `eventLog` -- append-only durable event log
- `chatClient` -- keyed `IChatClient` injected via `[Llm<TModel>]`
- `history` -- durable conversation history
- `trackingItems` -- durable dictionary for reminder-based tracking

## Adding a New LLM Model

LLM model agents wrap a specific model behind a grain interface. Three files are needed:

### 1. Create the LLMModel singleton

Add a new file in `src/Core/AI/Models/`. The class is a sealed singleton extending `LLMModel` with the model's provider and ID. The companion interface lets Orleans route grain calls.

```csharp
// src/Core/AI/Models/MyModel.cs
using Core.Contracts;

namespace Core.AI.Models;

public sealed class MyModel : LLMModel
{
    public static readonly MyModel Instance = new();
    private MyModel() { }

    public override string Id => "my-model-id";
    public override string DisplayName => "My Model";
    public override ProviderType Provider => ProviderType.OpenAI; // or Anthropic, Ollama, GitHub
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}

public interface IMyModel : IAgent { }
```

The `ServiceKey` is derived automatically from `Provider` and `Id` (e.g. `openai-my-model-id`).

### 2. Register the model in EnsureAllModelsLoaded

Add `_ = Models.MyModel.Instance;` to `LLMModel.EnsureAllModelsLoaded()` in `src/Core/AI/LLMModel.cs` so the singleton is force-loaded at startup.

### 3. Create the LLM agent

Add a new file in `src/Agents/LLM/`. The agent extends `IAW.Core.LLM` (which extends `Agent`) and implements the grain interface:

```csharp
// src/Agents/LLM/MyModelAgent.cs
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace IAW.Agents.LLM;

public class MyModelAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<MyModel>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : global::IAW.Core.LLM(state, eventLog, chatClient, history, trackingItems), IMyModel
{
    protected override string DisplayName => MyModel.Instance.DisplayName;
}
```

### 4. Register the mapper in AgentTest

Add `RegisterLlmMapper<MyModel>(siloBuilder, mockClient);` to `AgentTestSiloConfigurator.Configure()` in `src/IAW.Testing/AgentTest.cs` so tests can resolve the keyed client.

### 5. Declare the model in AppHost

In your AppHost, chain `.WithLLM<MyModel>()` on the Orleans service:

```csharp
var orleans = builder.AddIAW("agents")
    .WithLLM<MyModel>();
```

## Adding a New Memory Type

Memory agents extend `IAW.Core.Memory` (which extends `Agent`) and add an `IEmbeddingGenerator` for vector search. Each specialization defines its own collection name and instructions.

### 1. Define the grain interface

```csharp
// src/Agents/Memory/IMyMemory.cs
using Core.Contracts;

namespace IAW.Agents.Memory;

public interface IMyMemory : IAgent;
```

### 2. Create the memory agent

The agent extends `IAW.Core.Memory`, which provides built-in `Observe`, `Search`, `Consolidate`, `Decay`, and `Forget` operations on a durable `IDurableList<MemoryEntry>`.

```csharp
// src/Agents/Memory/MyMemoryAgent.cs
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Models;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace IAW.Agents.Memory;

public class MyMemoryAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : global::IAW.Core.Memory(state, eventLog, chatClient, history, trackingItems, memories, embedder), IMyMemory
{
    protected override string CollectionName => "iaw-my-memory";
    protected override string DisplayName => "My Memory";
    protected override string Instructions =>
        "You manage a specific type of knowledge. Store, search, and consolidate entries.";
}
```

The existing memory agents (`UserMemory`, `ProjectMemory`, `PatternMemory`, `EpisodeMemory`, `CodeMemory`) all follow this exact pattern -- the only differences are `CollectionName`, `DisplayName`, and `Instructions`.

## Writing Orchestration Scenarios

Orchestration in IAW uses three components: `OrchestrationPlan`, `ScriptGenerator`, and `CodeOrchestrator`.

### OrchestrationPlan

Define a plan as a sequence of `PlanStep` records, each targeting an agent type with an action and parameters:

```csharp
var plan = new OrchestrationPlan("Build and test the project", [
    new PlanStep(1, "DotNet", "Build the solution", new() { ["message"] = "dotnet build IAW.slnx" }),
    new PlanStep(2, "DotNet", "Run tests", new() { ["message"] = "dotnet test IAW.slnx" }),
    new PlanStep(3, "Reviewer", "Review results", new() { ["message"] = "Review the build output" })
]);
```

### ScriptGenerator

`ScriptGenerator.Generate()` converts an `OrchestrationPlan` into a runnable C# program that connects to the Orleans cluster and invokes agents in order. It uses `InterfaceCatalog.Discover()` to resolve grain interfaces and IDs automatically:

```csharp
string script = ScriptGenerator.Generate(plan, "127.0.0.1", 30000, workspace: "/path/to/project");
```

### OrchestrationCompiler

Before executing, validate the generated script with `OrchestrationCompiler.Compile()` (Roslyn-based). It parses the source and returns compilation errors without executing:

```csharp
var result = OrchestrationCompiler.Compile(script);
if (!result.Success)
    Console.WriteLine(string.Join("\n", result.Errors));
```

### ScriptExecutor

`ScriptExecutor` scaffolds a temporary console project, writes the generated source, and runs it:

```csharp
var executor = new ScriptExecutor();
var result = await executor.ExecuteScriptAsync(script, workingDirectory, ct: ct);
```

### CodeOrchestrator

`CodeOrchestratorAgent` provides durable task tracking. Create tasks, track their state, and pause/resume:

```csharp
var orchestrator = clusterClient.GetGrain<ICodeOrchestrator>("code-orchestrator");
var taskId = await orchestrator.CreateTask("Refactor the agent base class", ct);
var taskState = await orchestrator.GetTaskState(taskId, ct);
await orchestrator.PauseTask(taskId, ct);
await orchestrator.ResumeTask(taskId, ct);
```

### TaskSupervisor

`TaskSupervisorAgent` monitors active tasks for stalls. Register a task, report progress, and query health:

```csharp
var supervisor = clusterClient.GetGrain<ITaskSupervisor>("task-supervisor");
await supervisor.RegisterTask(taskId, "code-orchestrator", stepCount: 3, ct);
await supervisor.ReportProgress(taskId, completedSteps: 1, ct);
var health = await supervisor.GetTaskHealth(taskId, ct);
```

### InterfaceCatalog

`InterfaceCatalog.Discover()` uses reflection to find all `IAgent`-derived interfaces, their grain IDs, and their communication contracts (`IStreamProducer<T>`, `IStreamConsumer<T>`, `IReceiver<T>`). Use `InterfaceCatalog.ToPromptString()` to format the catalog for LLM consumption.

## Testing Agents

All agents must have tests. Use `AgentTest<T>` from `IAW.Testing` to get universal behavior tests automatically.

### Basic test class

```csharp
using IAW.Testing;

public class MyAgentTests : AgentTest<MyAgent>
{
    [Fact]
    public async Task CustomBehavior()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent(UniqueId("my-agent"));
        var response = await agent.GetResponse("Hello", ct);
        Assert.NotEmpty(response);
    }
}
```

### Testing requirements

- Every agent class must have a corresponding test class inheriting `AgentTest<T>`.
- Always use `TestContext.Current.CancellationToken` for cancellation tokens -- never `CancellationToken.None` or `default`. This ensures tests respect timeouts and are cancellable.
- Use `UniqueId("prefix")` to generate collision-free grain IDs across parallel test runs.
- Use `Agent(id)` to resolve the grain via the test cluster. It automatically finds the correct grain interface.
- The `AgentTestSiloConfigurator` registers in-memory storage, `MockChatClient` (returns `"mock-response"`), and all LLM model mappers. No external dependencies are needed.
- Run the full suite before submitting PRs:

```bash
dotnet test IAW.slnx
```

## Making Changes

1. Fork the repository
2. Create a feature branch: `git checkout -b feature/my-feature`
3. Make your changes
4. Ensure all tests pass: `dotnet test IAW.slnx`
5. Commit with a descriptive message
6. Push and open a Pull Request

## Code Style

- Follow the `.editorconfig` rules (enforced automatically by IDEs)
- Use self-explanatory C# naming -- no `/// <summary>` comments unless they add real value
- Only add inline comments in exceptional cases where logic isn't self-evident
- All serializable Orleans types need `[GenerateSerializer]` and `[Id(n)]` attributes

## Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/):
- `feat:` new feature
- `fix:` bug fix
- `refactor:` code change that neither fixes a bug nor adds a feature
- `docs:` documentation only
- `test:` adding or updating tests
- `chore:` maintenance tasks

## Questions?

Open an issue or start a discussion on GitHub.
