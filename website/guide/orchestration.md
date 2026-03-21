# Orchestration

IAW includes an orchestration subsystem for multi-step task execution. The `CodeOrchestratorAgent` generates and executes standalone C# console apps that connect to the Orleans cluster as clients, calling agent grains directly. The `ScriptGenerator` produces the project scaffold, the `OrchestrationCompiler` validates the generated code with Roslyn, and `dotnet run` executes it. This page covers each component and how they work together.

## CodeOrchestratorAgent

The `CodeOrchestratorAgent` is the orchestration engine. It discovers available agents, generates a standalone C# console app, compiles it, and runs it with `dotnet run`. The generated app connects to the cluster via `builder.AddIAWClient()` and calls agent grains to accomplish the task.

```csharp
var orchestrator = GrainFactory.GetGrain<ICodeOrchestrator>("orchestrator");
```

The agent's interface:

```csharp
public interface ICodeOrchestrator : IAgent;
```

### Workflow

1. The Thread agent receives a user request and calls the `Delegate` tool
2. `AgentSelectorAgent` picks the appropriate agents for the task
3. `CodeOrchestratorAgent` receives the plan and selected agents
4. It generates a standalone C# console app using the cluster connection helpers
5. The app is executed with `dotnet run`; stdout is captured for the result
6. An `OrchestrationResult` is returned and published to the `job.completed` stream

## Agent Registry Integration

The orchestration system discovers available agents through `IAgentRegistry`. Every concrete `Agent` subclass is automatically registered at silo startup by `AgentRegistrationStartupTask`.

```csharp
var registry = GrainFactory.GetGrain<IAgentRegistry>("global");

// Get all registered agents
var allAgents = await registry.GetAllAsync();

// Query by capabilities
var codeAgents = await registry.QueryAsync(new AgentQuery(
    Capabilities: ["code-review"]));
```

Each `AgentRecord` includes:

```csharp
[GenerateSerializer]
public record AgentRecord(
    string AgentType,
    string DisplayName,
    string Description,
    AgentKind Kind,
    AgentInterfaceMetadata[] Interfaces);
```

`AgentInterfaceMetadata` carries the interface type name, capabilities, published streams, and subscribed streams for each interface the agent implements. The `CodeOrchestratorAgent` uses this registry to match the task to the right agents.

## ScriptGenerator

`ScriptGenerator` converts the orchestration plan and selected agents into a standalone C# console project. The generated project:

- Creates an Orleans client connecting to the cluster via `builder.AddIAWClient()`
- Calls agent grains using `client.Get<TAgent>(taskId)` (resolved via `AgentRegistry` keyed to the task context)
- Calls methods like `shell.RunDotnetAsync()`, `fs.WriteFileAsync()`, etc.
- Writes a `result.json` file on completion

```csharp
var script = ScriptGenerator.Generate(plan, selectedAgents, taskId);
```

The generated script targets a console application and uses the full IAW client SDK, so generated code has access to all agent interfaces.

## OrchestrationCompiler

The `OrchestrationCompiler` uses Roslyn to validate generated scripts at compile time, catching errors before execution.

```csharp
using IAW.Agents.CSharp;

var compiler = new OrchestrationCompiler();

var additionalReferences = new[]
{
    MetadataReference.CreateFromFile(typeof(IAgent).Assembly.Location),
    MetadataReference.CreateFromFile(typeof(IClusterClient).Assembly.Location)
};

try
{
    var assembly = compiler.Compile(script, additionalReferences);
    // Script is valid -- proceed to execution
}
catch (InvalidOperationException ex)
{
    // ex.Message contains Roslyn compilation errors
    Console.WriteLine($"Script validation failed: {ex.Message}");
}
```

The compiler:
1. Parses the source code into a Roslyn `SyntaxTree`
2. Adds references to `System.Runtime`, `System.Collections`, and any additional references you provide
3. Creates a `CSharpCompilation` targeting a console application
4. Emits the assembly to a memory stream
5. If compilation fails, throws with the error diagnostics
6. If compilation succeeds, loads the assembly into a collectible `AssemblyLoadContext`

This validation step is critical for catching type mismatches, missing references, or syntax errors in LLM-generated orchestration code.

## OrchestrationResult

The `OrchestrationResult` record is the structured return type for every orchestration run:

```csharp
[GenerateSerializer]
public record OrchestrationResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Summary,
    [property: Id(2)] string[] Artifacts,
    [property: Id(3)] string? ErrorDetails = null);
```

`Success` indicates whether the run completed without errors. `Summary` is a human-readable description. `Artifacts` lists file paths or resource identifiers produced during execution. `ErrorDetails` is populated on failure.

The result is:
- Returned directly from the `CodeOrchestratorAgent` grain call
- Published to the `job.completed` Orleans stream for Telegram delivery
- Formatted as a structured card with follow-up buttons by `TelegramUIAgent`

## Progress Events

During execution, `CodeOrchestratorAgent` publishes `orchestration.progress` events at each phase:

| Phase | Event payload |
|-------|--------------|
| Planning | "Analyzing task and selecting agents..." |
| Building | "Generating orchestration code..." |
| Executing | "Running agents: {agentList}" |
| Completed | Final summary or error message |

These events are published via `PublishToStream<T>` and consumed by the Telegram `StreamSubscriber`, which edits a single progress message in-place rather than sending a new message per event.

## Full Orchestration Flow

```
User: "Create a calculator app at D:\IAW\Calc"
  |
  v
Thread agent → Delegate tool (schedules async job)
  |
  v
AgentSelectorAgent → picks IFileSystem, IDotNet, IRoslyn
  |
  v
CodeOrchestratorAgent:
  1. Generates standalone C# console app
  2. App connects to cluster via AddIAWClient()
  3. Calls agent grains: shell.RunDotnetAsync(), fs.WriteFileAsync(), etc.
  4. Executes with dotnet run, captures output
  5. Returns OrchestrationResult (success/failure, artifacts, metrics)
  |
  v
Progress events → orchestration.progress stream → Telegram live updates
  |
  v
OrchestrationResult → job.completed stream → structured card + buttons
```
