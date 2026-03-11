# Persistence

IAW agents use Orleans journaled grains for durable state. In development, all storage runs in-memory. For production, IAW supports CosmosDB for grain state, clustering, and reminders, and Qdrant for vector search. This page covers the storage architecture, configuration options, and how to switch between modes.

## Storage Architecture

Every agent's state is managed through three `[Memory]`-annotated durable collections:

| Collection | Type | Storage Key | Purpose |
|---|---|---|---|
| `state` | `IDurableDictionary<string, StateDescriptor>` | `agent-state` | General key-value state |
| `eventLog` | `IDurableList<AgentEvent>` | `agent-events` | Append-only event audit log |
| `trackingItems` | `IDurableDictionary<string, TrackingItem>` | `tracking-items` | Scheduled tracking items |

Orleans manages persistence automatically. Mutations are committed via `WriteStateAsync()`, which flushes the journaled grain's state to the configured storage provider.

```csharp
// Writing state inside an agent
State["my-key"] = new StateDescriptor("my-key", "my-value");
await WriteStateAsync(ct);

// State survives grain deactivation and silo restarts
```

## In-Memory Mode (Development)

The default configuration uses in-memory storage for everything. This is fast, requires no external dependencies, and is ideal for development and testing.

### AppHost Configuration

```csharp
using IAW.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW()   // configures in-memory storage
    .WithLLM<Sonnet46>();
```

`AddIAW()` configures:

```csharp
public static OrleansService AddIAW(
    this IDistributedApplicationBuilder builder,
    string name = "agents")
{
    var orleans = builder.AddOrleans(name)
        .WithDevelopmentClustering()           // localhost clustering
        .WithMemoryGrainStorage("Default")     // in-memory grain state
        .WithMemoryGrainStorage("PubSubStore") // streaming infrastructure
        .WithMemoryStreaming("agents")          // memory stream provider
        .WithMemoryReminders();                 // in-memory reminders

    return orleans;
}
```

### Silo Configuration for Tests

For unit tests with `TestCluster`, configure the same in-memory providers:

```csharp
public sealed class AgentSiloConfigurator : ISiloConfigurator
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
```

The `VolatileStateMachineStorageProvider` and `AddStateMachineStorage()` are required for Orleans journaled grains (`DurableGrain`), which is the base class for all IAW agents.

::: warning
In-memory storage is lost when the silo restarts. All agent state, conversation history, events, and tracking items will be reset. Use this mode only for development and testing.
:::

## CosmosDB Mode (Durable)

For production deployments, use Azure CosmosDB (or the CosmosDB emulator for local development) to persist grain state, clustering data, and reminders.

### AppHost Configuration with CosmosDB

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// CosmosDB emulator for local development
var cosmos = builder.AddAzureCosmosDB("cosmos")
    .RunAsEmulator();

var cosmosDb = cosmos.AddCosmosDatabase("iaw-db");

var iaw = builder.AddOrleans("agents")
    .WithCosmosClustering(cosmosDb)
    .WithCosmosGrainStorage("Default", cosmosDb)
    .WithCosmosGrainStorage("PubSubStore", cosmosDb)
    .WithCosmosReminders(cosmosDb)
    .WithMemoryStreaming("agents");

var silo = builder.AddProject<Projects.IAW_Silo>("silo")
    .WithReference(iaw)
    .WithReference(cosmosDb)
    .WithLLMEnvironment(builder);
```

This gives you:
- **Durable clustering** -- silo membership stored in CosmosDB
- **Durable grain state** -- all agent state survives silo restarts
- **Durable reminders** -- tracking items and scheduled checks persist
- **In-memory streaming** -- streams remain in-memory (Orleans streams are transient by design)

### CosmosDB Emulator

For local development without an Azure subscription, use the CosmosDB emulator. The Aspire `RunAsEmulator()` call automatically starts the emulator as a Docker container:

```bash
# The emulator starts automatically with aspire run
aspire run
```

The emulator provides a fully compatible CosmosDB API on `https://localhost:8081` with a well-known key for development.

### State Machine Storage with CosmosDB

Journaled grains require a state machine storage provider. For CosmosDB:

```csharp
// In silo Program.cs
builder.UseOrleans(silo =>
{
    silo.AddCosmosStateMachineStorage("cosmos-connection-string");
});
```

## Qdrant for Vector Search

Memory agents that perform semantic search use Qdrant as a vector database for embedding storage and similarity search.

### AppHost Configuration with Qdrant

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume("qdrant-data");

var silo = builder.AddProject<Projects.IAW_Silo>("silo")
    .WithReference(iaw)
    .WithReference(qdrant)
    .WithLLMEnvironment(builder);
```

### Silo Registration

In the silo's `Program.cs`, register the Qdrant client:

```csharp
builder.AddQdrantClient("qdrant");
```

### Local Embeddings

For local development without a cloud embedding service, use Ollama with a local embedding model:

```csharp
// In silo Program.cs
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
{
    var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:11434") };
    return new OllamaEmbeddingGenerator(httpClient, "nomic-embed-text");
});
```

### AppHost with Ollama for Embeddings

```csharp
var ollama = builder.AddOllama("ollama")
    .WithDataVolume("ollama-data")
    .AddModel("nomic-embed-text");

var silo = builder.AddProject<Projects.IAW_Silo>("silo")
    .WithReference(ollama);
```

## Configuration Comparison

| Component | In-Memory (Dev) | CosmosDB (Prod) |
|---|---|---|
| Grain state | `WithMemoryGrainStorage` | `WithCosmosGrainStorage` |
| Clustering | `WithDevelopmentClustering` | `WithCosmosClustering` |
| Reminders | `WithMemoryReminders` | `WithCosmosReminders` |
| Streaming | `WithMemoryStreaming` | `WithMemoryStreaming` |
| State machine | `VolatileStateMachineStorageProvider` | `CosmosStateMachineStorage` |
| Vector search | In-memory (keyword fallback) | Qdrant |
| Embeddings | None (optional Ollama) | Cloud API or Ollama |

## Serialization Requirements

All types stored in grain state must be Orleans-serializable:

```csharp
[GenerateSerializer]
public record StateDescriptor(
    [property: Id(0)] string Key,
    [property: Id(1)] object Value);

[GenerateSerializer]
public record AgentEvent(
    [property: Id(0)] string EventName,
    [property: Id(1)] string SourceAgentId,
    [property: Id(2)] string CorrelationId,
    [property: Id(3)] DateTimeOffset Timestamp,
    [property: Id(4)] Dictionary<string, object> Payload);
```

Every custom type passed between grains or stored in state must have:
1. `[GenerateSerializer]` attribute on the type
2. `[property: Id(n)]` on each property, with sequential IDs starting from 0

Missing these attributes causes Orleans serialization failures at runtime.

## Monitoring Storage

The Aspire dashboard shows storage health and performance metrics. Open it at the URL shown in the `aspire run` output (typically `https://localhost:15888`).

Key metrics to watch:
- Grain activation time -- slow activations may indicate storage latency
- State write duration -- tracked by the `agents.conversations.duration` histogram
- Event log size -- grows continuously, consider periodic archival

For CosmosDB, the emulator dashboard at `https://localhost:8081/_explorer/index.html` shows collection sizes, request units consumed, and query performance.
