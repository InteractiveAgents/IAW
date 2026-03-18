# Orchestration

IAW includes an orchestration subsystem for multi-step task execution. The `PlanningAgent` generates execution plans, the `ScriptGenerator` produces typed Orleans client scripts, the `OrchestrationCompiler` validates them with Roslyn, and the `ScriptExecutor` runs them. This page covers each component and how they work together.

## PlanningAgent

The `PlanningAgent` is the orchestration engine. It discovers available agents, generates execution plans, and runs them as standalone C# scripts that connect to the Orleans cluster.

```csharp
var planning = GrainFactory.GetGrain<IPlanning>("planning");
```

The agent's interface:

```csharp
public interface IPlanning : IAgent;
```

It operates through three LLM tools:

| Tool | Purpose |
|---|---|
| `QueryAgentsAsync` | Queries the agent registry for available agents and capabilities |
| `GeneratePlanAsync` | Creates an `OrchestrationPlan` from a summary and JSON steps |
| `ExecutePlanAsync` | Generates a C# script from the plan and executes it |

### Workflow

1. The user describes what they want done
2. PlanningAgent queries the agent registry to discover capabilities
3. PlanningAgent generates a plan with ordered steps
4. PlanningAgent generates a C# script and executes it

```csharp
// The PersonalAssistant delegates planning work to PlanningAgent
var message = new ChatMessage(
    "Plan and execute: run tests, review code, then deploy", ChatRole.User);

await foreach (var response in planning.SendMessage(message, ct))
{
    if (response.Kind == AgentResponseKind.Text)
        Console.Write(response.Content);
}
```

## OrchestrationPlan

An `OrchestrationPlan` is a serializable record containing a summary and an ordered list of steps:

```csharp
[GenerateSerializer]
public record OrchestrationPlan(
    [property: Id(0)] string Summary,
    [property: Id(1)] IReadOnlyList<PlanStep> Steps,
    [property: Id(2)] string? TaskId = null,
    [property: Id(3)] string? ProjectId = null,
    [property: Id(4)] Dictionary<string, string>? GlobalParameters = null);

[GenerateSerializer]
public record PlanStep(
    [property: Id(0)] int Order,
    [property: Id(1)] string AgentType,
    [property: Id(2)] string Action,
    [property: Id(3)] Dictionary<string, string> Parameters,
    [property: Id(4)] bool Critical = false);
```

The `TaskId` and `ProjectId` fields link the plan back to the originating task and project for tracking. `GlobalParameters` are merged into every step's parameters at execution time. The `Critical` flag on a step indicates that failure should abort the entire plan rather than continuing to subsequent steps.

Each `PlanStep` specifies which agent to invoke, what action to take, and a parameter dictionary. Parameters typically include `workspace` (the project path) and `message` (the instruction to send to the agent).

### Creating a Plan Manually

```csharp
var plan = new OrchestrationPlan(
    "Build, test, and deploy the project",
    [
        new PlanStep(1, "DotNet", "Build the solution",
            new() { ["workspace"] = "/src/project", ["message"] = "Build the solution" }),
        new PlanStep(2, "DotNet", "Run all tests",
            new() { ["workspace"] = "/src/project", ["message"] = "Run all tests" }),
        new PlanStep(3, "Deployer", "Deploy to staging",
            new() { ["message"] = "Deploy the latest build to staging" })
    ]);
```

## Agent Registry Integration

The orchestration system discovers available agents through the `AgentRegistryGrain`. Every concrete `Agent` subclass is automatically registered at silo startup by `AgentRegistrationStartupTask`.

```csharp
var registry = GrainFactory.GetGrain<IAgentRegistryGrain>("global");

// Get all registered agents
var allAgents = await registry.GetAllAsync();

// Query by capabilities
var codeAgents = await registry.QueryAsync(new AgentQuery(
    Capabilities: ["code-review"]));

// Query by subscriptions
var buildWatchers = await registry.QueryAsync(new AgentQuery(
    Subscribes: ["build.completed"]));
```

Each `AgentRegistration` includes:

```csharp
[GenerateSerializer]
public record AgentRegistration(
    string AgentType,
    string DisplayName,
    string Description,
    AgentKind Kind,
    string[] Capabilities,
    string[] Publishes,
    string[] Subscribes);
```

The PlanningAgent uses this registry to match user requests to the most appropriate agent for each step.

## ScriptGenerator

`ScriptGenerator` converts an `OrchestrationPlan` into a standalone C# program that connects to the Orleans cluster and executes each step sequentially. The generated script uses a structured event protocol for reporting progress back to the orchestrator.

### Event Protocol

Generated scripts emit structured markers on stdout so the orchestrator can parse progress:

| Marker | Format | Purpose |
|--------|--------|---------|
| `[PROGRESS]` | `[PROGRESS] Step {n}: {description}` | Reports that a step has started |
| `[ERROR]` | `[ERROR] Step {n}: {message}` | Reports a step failure |
| `[COMPLETED]` | `[COMPLETED] {summary}` | Reports successful orchestration completion |

These markers are parsed by the `ScriptExecutor` and translated into Orleans stream events.

```csharp
using IAW.Core.Orchestration;

var plan = new OrchestrationPlan("Run tests", [
    new PlanStep(1, "DotNet", "Test the solution",
        new() { ["workspace"] = "/src/project", ["message"] = "Run all tests" })
]);

var script = ScriptGenerator.Generate(plan, "localhost", 30000);
```

The generated script:
- Creates an Orleans client connecting to the specified cluster endpoint and gateway port
- For each step, resolves the agent grain by type name
- Sets the workspace if the `workspace` parameter is provided
- Sends the `message` parameter to the agent and streams the response

Example generated output:

```csharp
using Orleans;
using Orleans.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using IAW.Core;

// Plan: Run tests
// Steps: 1

var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleansClient(client =>
{
    client.UseStaticClustering(options =>
        options.Gateways.Add(new IPEndPoint(
            IPAddress.Parse("localhost"), 30000).ToGatewayUri()));
});

using var host = builder.Build();
await host.StartAsync();
var client = host.Services.GetRequiredService<IClusterClient>();
Console.WriteLine("Connected to cluster.");

// Step 1: Test the solution via DotNet
Console.WriteLine("Step 1: Test the solution");
var agent1 = client.GetGrain<IAgent>("orchestrated-dotnet");
await agent1.SetWorkspaceAsync("/src/project");
await foreach (var response in agent1.SendMessageAsync(
    new ChatMessage("Run all tests")))
{
    if (response.Kind == AgentResponseKind.Text)
        Console.Write(response.Content);
}
Console.WriteLine();

await host.StopAsync();
Console.WriteLine("Orchestration complete.");
```

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

This validation step is critical for catching type mismatches, missing references, or syntax errors in LLM-generated orchestration scripts.

## ScriptExecutor

`ScriptExecutor` runs a generated script as a standalone .NET process:

```csharp
using IAW.Core.Orchestration;

var executor = new ScriptExecutor();
var result = await executor.ExecuteScriptAsync(
    programSource: script,
    workingDirectory: "/tmp/orchestration",
    ct: cancellationToken);

if (result.Success)
    Console.WriteLine($"Output: {result.Output}");
else
    Console.WriteLine($"Failed (exit {result.ExitCode}): {result.Output}");
```

The execution process:
1. Creates a timestamped directory under the working directory
2. Scaffolds a new console project with `dotnet new console`
3. Replaces `Program.cs` with the generated script
4. Runs the project with `dotnet run`
5. Captures stdout and stderr

The result is a `ScriptResult`:

```csharp
[GenerateSerializer]
public record ScriptResult(
    [property: Id(0)] int ExitCode,
    [property: Id(1)] string Output)
{
    public bool Success => ExitCode == 0;
}
```

### Streaming Overload

`ScriptExecutor` also provides a streaming overload that yields output lines as they are produced, rather than waiting for the process to complete:

```csharp
await foreach (var line in executor.ExecuteScriptStreamingAsync(
    programSource: script,
    workingDirectory: "/tmp/orchestration",
    ct: cancellationToken))
{
    // Parse [PROGRESS], [ERROR], [COMPLETED] markers in real-time
    Console.WriteLine(line);
}
```

This is used by the orchestration system to publish `orchestration.progress` stream events in real-time.

## CheckpointStore

The `CheckpointStore` provides blob-based persistence for orchestration state, allowing long-running plans to resume after failures:

```csharp
var store = new CheckpointStore(blobContainerClient);

// Save checkpoint after each step completes
await store.SaveAsync(planId, checkpoint);

// Restore checkpoint on restart
var checkpoint = await store.LoadAsync(planId);
```

Checkpoints record which steps have completed, their outputs, and any accumulated state. When a plan resumes, already-completed steps are skipped.

## CodeOrchestratorAgent

The `CodeOrchestratorAgent` is a supervisor that wraps the PlanningAgent with self-healing capabilities. When a step fails, the orchestrator:

1. Captures the error output
2. Sends it back to the LLM for diagnosis
3. Generates a corrective step or modified plan
4. Retries the failed step with the fix applied

This self-healing loop runs up to a configurable retry limit per step. If all retries are exhausted, the step is marked as failed and the orchestrator either aborts (if the step is `Critical`) or continues to the next step.

## Orchestration Event Types

The orchestration system publishes typed events to Orleans streams for real-time monitoring:

| Event Type | Stream | Purpose |
|-----------|--------|---------|
| `OrchestrationProgressEvent` | `orchestration.progress` | Step started or completed with output |
| `OrchestrationErrorEvent` | `orchestration.progress` | Step failure with error details |
| `OrchestrationCompletedEvent` | `orchestration.completed` | Entire plan finished (success or partial failure) |

These events are consumed by the Telegram `StreamSubscriber` and the DevUI to show real-time orchestration status.

## PersonalAssistant as Coordinator

The `PersonalAssistantAgent` sits above the orchestration system as the entry point for user requests. It delegates planning work to `PlanningAgent` and direct tasks to specific agents.

```csharp
public class PersonalAssistantAgent : Agent,
    IPersonalAssistant,
    IReceiver<TaskCompletedMessage>,
    IReceiver<TaskFailedMessage>,
    IReceiver<ReviewCompletedMessage>,
    IReceiver<DeploySucceededMessage>
{
    // Tools available to the LLM:
    // - AssignTaskToAgent: send a task to a specific agent by grain key
    // - GetTeamStatusTool: query the registry for all agents and their state
    // - SpawnDynamicAgent: create a new dynamic agent for parallel work
}
```

The PersonalAssistant resolves agents by grain key. It maintains a mapping of well-known agent keys to their specific grain interfaces:

| Key | Interface | Agent |
|---|---|---|
| `reviewer` | `IReviewer` | Code review |
| `self-improvement` | `ISelfImprovement` | Metrics analysis and code improvement |
| `deployer` | `IDeployer` | Release builds and deployment |
| `planning` | `IPlanning` | Orchestration plan generation |
| `roslyn` | `IRoslyn` | C# code intelligence |
| `dot-net` | `IDotNet` | .NET toolchain |
| `nu-get` | `INuGet` | Package management |
| `git-hub` | `IGitHub` | GitHub API |

The Project agent's `DelegateToAssistant` tool connects Telegram users to the PersonalAssistant. When a user's request requires multi-agent coordination, the Project agent calls `DelegateToAssistant`, which forwards the task to PersonalAssistant for decomposition and delegation to the appropriate specialized agents.

## Full Orchestration Flow

```
User: "Run tests, review the results, then deploy if green"
  |
  v
Project agent → DelegateToAssistant (if from Telegram)
  |
  v
PersonalAssistant --> PlanningAgent.SendMessage(...)
  |
  v
PlanningAgent:
  1. QueryAgentsAsync() --> discovers DotNet, Reviewer, Deployer
  2. GeneratePlanAsync("Test, review, deploy", [...steps...])
  3. ExecutePlanAsync("localhost", 30000)
     |
     v
  ScriptGenerator.Generate(plan, "localhost", 30000)
     |
     v
  OrchestrationCompiler.Compile(script, references)  // optional validation
     |
     v
  ScriptExecutor.ExecuteScriptStreamingAsync(script, workingDir)
     |
     v
  [dotnet new console] --> [replace Program.cs] --> [dotnet run]
     |
     v
  Script connects to cluster, invokes agents in order:
    Step 1: DotNet.SendMessage("Run all tests")       → [PROGRESS] Step 1: ...
    Step 2: Reviewer.SendMessage("Review test results") → [PROGRESS] Step 2: ...
    Step 3: Deployer.SendMessage("Deploy to staging")   → [COMPLETED] ...
     |
     v                          (on failure)
  CodeOrchestratorAgent:        ←──────────┐
    - Captures [ERROR] output              |
    - Sends to LLM for diagnosis           |
    - Generates corrective step            |
    - Retries (self-healing loop) ─────────┘
     |
     v
  OrchestrationProgressEvent → orchestration.progress stream
  OrchestrationCompletedEvent → orchestration.completed stream
     |
     v
  StreamSubscriber (Telegram) delivers real-time updates to user
```
