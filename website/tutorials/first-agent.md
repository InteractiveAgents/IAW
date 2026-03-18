# Build Your First Agent

This tutorial walks you through creating an IAW agent from scratch, registering it in the Aspire AppHost, and testing it.

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

Create a file `WeatherAgent.cs`:

```csharp
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

public interface IWeatherAgent : IAgent { }

public class WeatherAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), IWeatherAgent
{
    protected override string DisplayName => "Weather";
    protected override string Instructions =>
        "You are a weather assistant. Provide current weather information.";
}
```

## Step 3: Add HTTP Endpoints

Update `Program.cs`:

```csharp
using IAW.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddIAW();

var app = builder.Build();

app.MapGet("/weather/metadata", async (IGrainFactory grains) =>
{
    var agent = grains.GetGrain<IWeatherAgent>("weather-agent");
    return await agent.GetMetadata(default);
});

app.MapPost("/weather/ask", async (IGrainFactory grains, ChatRequest request) =>
{
    var agent = grains.GetGrain<IWeatherAgent>("weather-agent");
    var response = await agent.GetResponse(request.Prompt, default);
    return new { response };
});

app.MapGet("/weather/events", async (IGrainFactory grains) =>
{
    var agent = grains.GetGrain<IWeatherAgent>("weather-agent");
    return await agent.GetEventLog(default);
});

app.Run();

record ChatRequest(string Prompt);
```

## Step 4: Create the AppHost

Create an Aspire AppHost project or add to an existing one:

```csharp
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>();

builder.AddProject<Projects.MyAgentSilo>("silo")
    .WithReference(iaw);

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
curl http://localhost:5000/weather/metadata

# Ask the agent about weather
curl -X POST http://localhost:5000/weather/ask \
  -H "Content-Type: application/json" \
  -d '{"prompt": "What is the current weather in New York?"}'

# Ask the agent another question
curl -X POST http://localhost:5000/weather/ask \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Will it rain tomorrow?"}'

# View events
curl http://localhost:5000/weather/events
```

## Step 8: Write a Unit Test

Create a test project and write a test:

```csharp
using IAW.Testing;
using Xunit;

public sealed class WeatherAgentTests : AgentTest<WeatherAgent>
{
    [Fact]
    public async Task Metadata_ReturnsWeather()
    {
        var agent = Agent("weather-test");
        var metadata = await agent.GetMetadata(TestContext.Current.CancellationToken);

        Assert.Equal("Weather", metadata.DisplayName);
    }

    [Fact]
    public async Task GetResponse_ReturnsText()
    {
        var agent = Agent("weather-response-test");
        var response = await agent.GetResponse("What's the weather?", TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrEmpty(response));
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
