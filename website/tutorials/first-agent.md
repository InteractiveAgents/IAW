# Build Your First Agent

This tutorial walks you through creating a V3 IAW agent from scratch, registering it in the Aspire AppHost, and testing it.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling)
- An Anthropic API key (for LLM integration)

## Step 1: Create the Project

Create a new .NET project that will host your agent as an Orleans silo:

```bash
dotnet new web -n MyAgentSilo
cd MyAgentSilo
dotnet add package IAW.Core
```

## Step 2: Define the Agent

Create a file `TodoAgent.cs`:

```csharp
using System.ComponentModel;
using Core.V3;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public interface ITodoAgent : IAgent;

public class TodoAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), ITodoAgent
{
    protected override string Instructions =>
        "You are a todo list manager. Help users create, list, and complete tasks.";

    protected override string DisplayName => "Todo Manager";

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(AddTodo),
        AIFunctionFactory.Create(ListTodos),
        AIFunctionFactory.Create(CompleteTodo)
    ];

    [Description("Add a new todo item")]
    private async Task<string> AddTodo([Description("Todo title")] string title)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        State[$"todo:{id}"] = new StateEntry($"todo:{id}", title);
        await WriteStateAsync(AgentCancellation);

        await PublishAsync("todo.added", new Dictionary<string, object>
        {
            ["id"] = id, ["title"] = title
        }, AgentCancellation);

        return $"Added todo '{title}' with ID {id}";
    }

    [Description("List all todo items")]
    private Task<string> ListTodos()
    {
        var todos = State
            .Where(kv => kv.Key.StartsWith("todo:"))
            .Select(kv => $"- [{kv.Key[5..]}] {kv.Value.Value}");
        var list = string.Join("\n", todos);
        return Task.FromResult(string.IsNullOrEmpty(list) ? "No todos found." : list);
    }

    [Description("Mark a todo as complete")]
    private async Task<string> CompleteTodo(
        [Description("Todo ID to complete")] string id)
    {
        var key = $"todo:{id}";
        if (!State.ContainsKey(key))
            return $"Todo {id} not found.";

        var title = State[key].Value;
        State[$"done:{id}"] = new StateEntry($"done:{id}", title);
        State.Remove(key);
        await WriteStateAsync(AgentCancellation);

        await PublishAsync("todo.completed", new Dictionary<string, object>
        {
            ["id"] = id, ["title"] = title
        }, AgentCancellation);

        return $"Completed todo '{title}'";
    }
}
```

## Step 3: Add HTTP Endpoints

Update `Program.cs`:

```csharp
using Core.AI;
using Core.V3;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.UseOrleans();
builder.Services.AddLlmProviders(builder);

var app = builder.Build();

app.MapGet("/todo/metadata", async (IGrainFactory grains) =>
{
    var agent = grains.GetGrain<ITodoAgent>("todo-agent");
    return await agent.GetMetadataAsync(default);
});

app.MapPost("/todo/ask", async (IGrainFactory grains, ChatRequest request) =>
{
    var agent = grains.GetGrain<ITodoAgent>("todo-agent");
    var response = await agent.GetResponse(request.Prompt, default);
    return new { response };
});

app.MapGet("/todo/history", async (IGrainFactory grains) =>
{
    var agent = grains.GetGrain<ITodoAgent>("todo-agent");
    return await agent.GetHistory(default);
});

app.MapGet("/todo/events", async (IGrainFactory grains) =>
{
    var agent = grains.GetGrain<ITodoAgent>("todo-agent");
    return await agent.GetEventLogAsync(default);
});

app.Run();

record ChatRequest(string Prompt);
```

## Step 4: Create the AppHost

Create an Aspire AppHost project or add to an existing one:

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>();

builder.AddProject<Projects.MyAgentSilo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.Build().Run();
```

## Step 5: Configure the API Key

```bash
cd src/IAW.AppHost
dotnet user-secrets set "Parameters:anthropic-api-key" "sk-ant-your-key-here"
```

## Step 6: Run

```bash
aspire run
```

Open the Aspire dashboard (typically at `https://localhost:17293`) to see your silo running.

## Step 7: Test via HTTP

```bash
# Get agent metadata
curl http://localhost:5000/todo/metadata

# Ask the agent to add a todo
curl -X POST http://localhost:5000/todo/ask \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Add a todo to buy groceries"}'

# Ask the agent to list todos
curl -X POST http://localhost:5000/todo/ask \
  -H "Content-Type: application/json" \
  -d '{"prompt": "List all my todos"}'

# View conversation history
curl http://localhost:5000/todo/history

# View events
curl http://localhost:5000/todo/events
```

## Step 8: Write a Unit Test

Create a test project and write a test:

```csharp
using Core.V3;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

public sealed class TodoAgentTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    [Fact]
    public async Task Metadata_ReturnsTodoManager()
    {
        var agent = _cluster.GrainFactory.GetGrain<ITodoAgent>("todo-test");
        var metadata = await agent.GetMetadataAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Todo Manager", metadata.DisplayName);
    }

    [Fact]
    public async Task GetResponse_ReturnsText()
    {
        var agent = _cluster.GrainFactory.GetGrain<ITodoAgent>("conv-test");
        var response = await agent.GetResponse("Hello", TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(response));
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder
                .AddMemoryGrainStorage("Default")
                .AddMemoryGrainStorage("PubSubStore")
                .AddMemoryStreams("agents")
                .UseInMemoryReminderService();

            siloBuilder.Services.AddSingleton<IStateMachineStorageProvider,
                VolatileStateMachineStorageProvider>();
            siloBuilder.AddStateMachineStorage();
        }
    }
}
```

Run the tests:

```bash
dotnet test
```

## Next Steps

- [Building Agents](/guide/agents) -- all override points and behavior interfaces
- [Events & Streams](/guide/events-streams) -- connect agents with typed event pipelines
- [Tools](/guide/behaviors/tools) -- built-in tools and custom tool creation
- [Testing](/guide/testing) -- comprehensive testing patterns
