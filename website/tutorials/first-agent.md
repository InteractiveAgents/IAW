# Build Your First Agent

This tutorial walks you through creating an IAW agent from scratch, registering it in the Aspire AppHost, and testing it.

## Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) installed
- [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling) installed
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
using Core.AI;
using Core.AI.Models;
using Core.V2;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

public class TodoAgent(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : AgentV2(messages, memory, events, subscriptions, notifications, tracking)
{
    protected override AgentProfile Profile => new()
    {
        Id = this.GetPrimaryKeyString(),
        DisplayName = "Todo Manager",
        Instructions = "You are a todo list manager. Help users create, list, and complete tasks. Use the available tools to manage the todo list.",
        Capabilities = ["todos", "task-management"]
    };

    protected override Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct = default)
        => RespondWithLlmAsync(chatClient, request, ct);

    protected override IReadOnlyList<AITool> DefineTools() =>
    [
        AIFunctionFactory.Create(AddTodo, "add_todo", "Add a new todo item"),
        AIFunctionFactory.Create(ListTodos, "list_todos", "List all todo items"),
        AIFunctionFactory.Create(CompleteTodo, "complete_todo", "Mark a todo as complete")
    ];

    private async Task<string> AddTodo(string title)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        await SetMemoryAsync($"todo:{id}", title);

        await AppendEventAsync(new AgentEvent
        {
            Type = "todo.added",
            Payload = $"{{\"id\":\"{id}\",\"title\":\"{title}\"}}"
        });

        return $"Added todo '{title}' with ID {id}";
    }

    private async Task<string> ListTodos()
    {
        var allMemory = Memory
            .Where(kv => kv.Key.StartsWith("todo:"))
            .Select(kv => $"- [{kv.Key[5..]}] {kv.Value}");

        var list = string.Join("\n", allMemory);
        return string.IsNullOrEmpty(list) ? "No todos found." : list;
    }

    private async Task<string> CompleteTodo(string id)
    {
        var key = $"todo:{id}";
        if (!Memory.ContainsKey(key))
            return $"Todo {id} not found.";

        var title = Memory[key];
        await SetMemoryAsync($"done:{id}", title);

        await AppendEventAsync(new AgentEvent
        {
            Type = "todo.completed",
            Payload = $"{{\"id\":\"{id}\",\"title\":\"{title}\"}}"
        });

        return $"Completed todo '{title}'";
    }
}
```

## Step 3: Add HTTP Endpoints

Update `Program.cs` to register LLM providers and expose the agent via HTTP:

```csharp
using Core.AI;
using Core.V2;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.UseOrleans();
builder.Services.AddLlmProviders(builder);

var app = builder.Build();

app.MapGet("/todo/profile", async (IGrainFactory grains) =>
{
    var agent = grains.GetGrain<IAgentV2>("todo-agent");
    return await agent.GetProfileAsync();
});

app.MapPost("/todo/ask", async (IGrainFactory grains, AgentRequest request) =>
{
    var agent = grains.GetGrain<IAgentV2>("todo-agent");
    return await agent.RespondAsync(request);
});

app.MapGet("/todo/events", async (IGrainFactory grains) =>
{
    var agent = grains.GetGrain<IAgentV2>("todo-agent");
    return await agent.QueryEventsAsync(new AgentEventQuery { Limit = 20, Descending = true });
});

app.Run();
```

## Step 4: Create the AppHost

Create an Aspire AppHost project or add to an existing one:

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>();

builder.AddProject<Projects.MyAgentSilo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

builder.Build().Run();
```

## Step 5: Configure the API Key

Set up your Anthropic API key:

```bash
cd src/IAW.AppHost
dotnet user-secrets set "Parameters:anthropic-api-key" "sk-ant-your-key-here"
```

## Step 6: Run

```bash
aspire run
```

Open the Aspire dashboard (typically at `https://localhost:17293`) to see your silo running.

## Step 7: Test

Use curl or any HTTP client to interact with your agent:

```bash
# Get agent profile
curl http://localhost:5000/todo/profile

# Ask the agent to add a todo
curl -X POST http://localhost:5000/todo/ask \
  -H "Content-Type: application/json" \
  -d '{"input": "Add a todo to buy groceries"}'

# Ask the agent to list todos
curl -X POST http://localhost:5000/todo/ask \
  -H "Content-Type: application/json" \
  -d '{"input": "List all my todos"}'

# View events
curl http://localhost:5000/todo/events
```

## Step 8: Write a Unit Test

Create a test project:

```bash
dotnet new xunit -n MyAgent.Tests
cd MyAgent.Tests
dotnet add reference ../MyAgentSilo/MyAgentSilo.csproj
dotnet add package Microsoft.Orleans.TestingHost
dotnet add package Microsoft.Orleans.Journaling
dotnet add package Microsoft.Orleans.Persistence.Memory
dotnet add package Microsoft.Orleans.Reminders
dotnet add package Microsoft.Orleans.Streaming
```

Write a test:

```csharp
using Core.V2;
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
    public async Task Profile_ReturnsTodoManager()
    {
        var agent = _cluster.GrainFactory.GetGrain<IAgentV2>("todo-test");
        var profile = await agent.GetProfileAsync();

        Assert.Equal("Todo Manager", profile.DisplayName);
        Assert.Contains("todos", profile.Capabilities);
    }

    [Fact]
    public async Task Memory_PersistsAcrossCalls()
    {
        var agent = _cluster.GrainFactory.GetGrain<IAgentV2>("memory-test");

        await agent.SetMemoryAsync("key", "value");
        var result = await agent.GetMemoryAsync("key");

        Assert.Equal("value", result);
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

            siloBuilder.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
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

- [Building Agents](/guide/agents) -- learn about all AgentV2 override points
- [Notifications & Events](/guide/notifications) -- add pub/sub communication between agents
- [Testing](/guide/testing) -- comprehensive testing patterns with TestCluster and Aspire
- [MCP Server](/guide/mcp) -- orchestrate your agent from Claude Code
