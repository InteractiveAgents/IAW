# Testing

IAW provides testing infrastructure for agents using `AgentTest<T>` (Orleans `TestCluster`) and `AspireAgentTest<T>` (Aspire `DistributedApplicationTestingBuilder`). Tests use xUnit v3 with `TestContext.Current.CancellationToken` for cooperative cancellation.

## AgentTest&lt;T&gt; (Unit Tests)

`AgentTest<T>` spins up an in-process Orleans cluster with in-memory storage, streams, and mock services. Fast and requires no external dependencies.

### Project Setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\IAW.Testing\IAW.Testing.csproj" />
    <ProjectReference Include="..\..\src\Agents\Agents.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

### Basic Test Class

Inherit from `AgentTest<T>` where `T` is the concrete agent type. The base class manages the full `TestCluster` lifecycle and auto-registers `MockChatClient`, `MockEmbeddingGenerator`, and all LLM model mappers:

```csharp
using IAW.Core;
using IAW.Testing;
using Xunit;

public class MyAgentTests : AgentTest<MyAgent>
{
    [Fact]
    public async Task GetResponse_returns_text()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("my-agent-1");

        var response = await agent.GetResponse("Hello!", ct);

        Assert.NotNull(response);
    }

    [Fact]
    public async Task Metadata_returns_correct_name()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("my-agent-2");

        var meta = await agent.GetMetadata(ct);

        Assert.Equal("My Agent", meta.DisplayName);
    }
}
```

Key features of `AgentTest<T>`:

- **Auto-wired cluster**: `TestCluster` is built and deployed in `InitializeAsync`, disposed in `DisposeAsync`
- **Agent resolution**: `Agent(id)` resolves the correct grain interface for `TAgent` automatically
- **Unique IDs**: `UniqueId(prefix)` generates test-run-scoped IDs to avoid grain collisions
- **Extensible**: Override `ConfigureSilo(TestClusterBuilder)` to add custom services or `OnClusterReadyAsync()` for setup after deployment

### What AgentTestSiloConfigurator Registers

The built-in silo configurator auto-registers everything needed for all agent tiers:

| Service | Registration | Purpose |
|---|---|---|
| `IChatClient` | `MockChatClient` (singleton) | Mock LLM for all agents |
| `IEmbeddingGenerator<string, Embedding<float>>` | `MockEmbeddingGenerator` (singleton) | Mock embeddings for Memory agents |
| `IStateMachineStorageProvider` | `VolatileStateMachineStorageProvider` | In-memory durable state |
| `IGitHubClient` | `Octokit.GitHubClient` (test header) | Mock GitHub client |
| `LlmAttributeMapper<TModel>` | Per-model keyed `IChatClient` | Resolves `[Llm<TModel>]` attributes |

::: tip
You do not need to register `MockEmbeddingGenerator` yourself. `AgentTestSiloConfigurator` registers it automatically, so `AgentTest<T>` works for Memory agents out of the box.
:::

### Testing Memory Agents

Memory agents (extending `Memory`) work with `AgentTest<T>` without any extra setup. The `MockEmbeddingGenerator` returns zero-vector embeddings of dimension 384:

```csharp
public class UserMemoryAgentTests : AgentTest<UserMemoryAgent>
{
    [Fact]
    public async Task UserMemory_metadata_correct()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("user-memory");
        var meta = await agent.GetMetadata(ct);
        Assert.Equal("User Memory", meta.DisplayName);
    }

    [Fact]
    public async Task UserMemory_responds_to_GetResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("user-memory");
        var response = await agent.GetResponse("hello", ct);
        Assert.NotNull(response);
    }
}
```

All five Memory agents (User, Project, Pattern, Episode, Code) follow this same pattern.

### Testing CodeOrchestrator

`CodeOrchestratorAgent` implements `ICodeOrchestrator` with task lifecycle methods. Cast the `IAgent` reference to `ICodeOrchestrator` to test orchestration-specific APIs:

```csharp
public class CodeOrchestratorTests : AgentTest<CodeOrchestratorAgent>
{
    [Fact]
    public async Task CreateTask_returns_task_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var taskId = await ((ICodeOrchestrator)agent).CreateTask("Fix build errors", ct);
        Assert.NotNull(taskId);
        Assert.StartsWith("task-", taskId);
    }

    [Fact]
    public async Task GetTaskState_returns_created_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var orch = (ICodeOrchestrator)agent;
        var taskId = await orch.CreateTask("Test task", ct);
        var state = await orch.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Created, state.Status);
        Assert.Equal("Test task", state.Description);
    }

    [Fact]
    public async Task PauseTask_updates_status()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = Agent("code-orchestrator");
        var orch = (ICodeOrchestrator)agent;
        var taskId = await orch.CreateTask("Pausable task", ct);
        await orch.PauseTask(taskId, ct);
        var state = await orch.GetTaskState(taskId, ct);
        Assert.Equal(OrchestrationStatus.Paused, state.Status);
    }
}
```

This pattern applies to any agent with a specialized grain interface: get the agent via `Agent(id)`, cast to the specific interface, and test the extended API.

### Testing Auto-Logging

Verify that LLM calls are automatically logged in the event log:

```csharp
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
}
```

## Architecture Guard Tests

Architecture guard tests use reflection to enforce design constraints across the codebase. They run without a `TestCluster` (plain `[Fact]` methods) and validate structural invariants.

### Core Guards (ArchitectureGuardTests)

```csharp
public class ArchitectureGuardTests
{
    [Fact]
    public void Agent_ExtendsDurableGrain()
    {
        var baseType = typeof(Agent).BaseType;
        Assert.Equal("DurableGrain", baseType!.Name);
    }

    [Fact]
    public void AllEventTypes_ImplementIEvent()
    {
        var eventTypes = typeof(Agent).Assembly.GetTypes()
            .Where(t => t.Namespace == "Core.Messages"
                && t.Name.EndsWith("Event") && !t.IsInterface);

        Assert.NotEmpty(eventTypes);
        foreach (var type in eventTypes)
            Assert.True(typeof(IEvent).IsAssignableFrom(type));
    }

    [Fact]
    public void AllSerializableTypes_HaveGenerateSerializerAttribute()
    {
        var messageRecords = typeof(Agent).Assembly.GetTypes()
            .Where(t => t.Namespace == "Core.Messages" && !t.IsInterface && !t.IsAbstract);

        foreach (var type in messageRecords)
            Assert.NotNull(type.GetCustomAttribute<GenerateSerializerAttribute>());
    }

    [Fact]
    public void IStreamConsumer_GenericConstraint_RequiresIEvent()
    {
        var constraint = typeof(IStreamConsumer<>).GetGenericArguments()[0]
            .GetGenericParameterConstraints();
        Assert.Contains(typeof(IEvent), constraint);
    }

    [Fact]
    public void NoCoreSourceFiles_ContainXmlDocSummary()
    {
        // Scans all *.cs files under src/Core for /// <summary> violations
    }

    [Fact]
    public void AllAgentsInIAWAgents_ExtendAgent()
    {
        var agentsAssembly = typeof(FileSystemAgent).Assembly;
        var concreteGrains = agentsAssembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface
                && t.Name.EndsWith("Agent") && typeof(IGrain).IsAssignableFrom(t));

        foreach (var type in concreteGrains)
            Assert.True(typeof(Agent).IsAssignableFrom(type));
    }
}
```

### V2 Guards (ArchitectureGuardV2Tests)

These validate the three-tier hierarchy and v0.2.0 additions:

```csharp
public class ArchitectureGuardV2Tests
{
    [Fact]
    public void LLM_agents_extend_LLM_base()
    {
        var llmAgents = typeof(PersonalAssistantAgent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(LLM)) && !t.IsAbstract);
        Assert.NotEmpty(llmAgents);
    }

    [Fact]
    public void Memory_agents_extend_Memory_base()
    {
        var memoryAgents = typeof(PersonalAssistantAgent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Memory)) && !t.IsAbstract);
        Assert.NotEmpty(memoryAgents);
    }

    [Fact]
    public void All_task_stream_events_have_TaskId()
    {
        var taskEventTypes = typeof(StepProgressEvent).Assembly.GetTypes()
            .Where(t => t.IsAssignableTo(typeof(ITaskStreamEvent)) && !t.IsInterface);

        Assert.All(taskEventTypes, t =>
        {
            var prop = t.GetProperty("TaskId");
            Assert.NotNull(prop);
        });
    }

    [Fact]
    public void InterfaceCatalog_discovers_LLM_agents()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.Contains(catalog, e => e.InterfaceName == "IOpus46");
        Assert.Contains(catalog, e => e.InterfaceName == "ISonnet46");
    }

    [Fact]
    public void InterfaceCatalog_discovers_Memory_agents()
    {
        var catalog = InterfaceCatalog.Discover();
        Assert.Contains(catalog, e => e.InterfaceName == "IUserMemory");
        Assert.Contains(catalog, e => e.InterfaceName == "IProjectMemory");
    }

    [Fact]
    public void All_agents_have_matching_IAgent_derived_interfaces()
    {
        var agentTypes = typeof(PersonalAssistantAgent).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Agent)) && !t.IsAbstract);

        foreach (var agent in agentTypes)
        {
            var hasSpecificInterface = agent.GetInterfaces()
                .Any(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i));
            Assert.True(hasSpecificInterface);
        }
    }
}
```

## Integration Tests with Aspire

Integration tests run the full Aspire AppHost and test against live endpoints and a real Orleans cluster.

### Project Setup

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Core\Core.csproj" />
    <ProjectReference Include="..\..\src\IAW.AppHost\Aspire.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
</Project>
```

### Test Class Structure

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

public sealed class AgentIntegrationTests : IAsyncLifetime
{
    private DistributedApplication _app = null!;
    private IHost _orleansClientHost = null!;
    private IClusterClient _orleansClient = null!;

    public async ValueTask InitializeAsync()
    {
        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aspire>(
            ["--Parameters:anthropic-api-key=test-key"]);

        _app = await appHost.BuildAsync();

        using var startTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _app.StartAsync(startTimeout.Token);
        await _app.ResourceNotifications
            .WaitForResourceAsync("samples", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromSeconds(60), startTimeout.Token);

        var gatewayEndpoint = _app.GetEndpoint("samples", "orleans-gateway");

        _orleansClientHost = Host.CreateApplicationBuilder()
            .UseOrleansClient(client =>
            {
                client.UseLocalhostClustering(
                    gatewayPort: gatewayEndpoint.Port,
                    serviceId: "default",
                    clusterId: "default");
                client.AddMemoryStreams("agents");
            })
            .Build();

        await _orleansClientHost.StartAsync(startTimeout.Token);
        _orleansClient = _orleansClientHost.Services.GetRequiredService<IClusterClient>();
    }

    public async ValueTask DisposeAsync()
    {
        await _orleansClientHost.StopAsync();
        _orleansClientHost.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Agent_ReturnsMetadata()
    {
        var ct = TestContext.Current.CancellationToken;
        var agent = _orleansClient.GetGrain<IAgent>("integration-test");
        var metadata = await agent.GetMetadata(ct);
        Assert.NotNull(metadata);
    }
}
```

::: warning Integration Test Requirements
Integration tests start the full Aspire AppHost, which requires Docker to be running for any container resources. The `DistributedApplicationTestingBuilder` spins up the application and waits for resources to become healthy before running tests.
:::

## Running Tests

```bash
# Run all tests
dotnet test IAW.slnx

# Run unit tests only
dotnet test test/Core.Tests/IAW.Core.Tests.csproj

# Run integration tests only
dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj

# Run a single test
dotnet test IAW.slnx --filter "FullyQualifiedName~GetResponse_returns_text"

# Run architecture guards only
dotnet test IAW.slnx --filter "FullyQualifiedName~ArchitectureGuard"
```
