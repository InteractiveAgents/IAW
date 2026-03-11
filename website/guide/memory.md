# Memory

IAW provides a memory subsystem for agents that need to store, search, and manage long-term knowledge. Memory agents extend the base `Agent` with embedding-based storage, provenance tracking, and automatic context injection. This page covers the memory architecture, the five built-in memory agents, memory operations, and context providers.

## Architecture

The memory system builds on top of the standard `Agent` base class. A memory agent stores `MemoryEntry` records in its durable state, generates embeddings for semantic search, and exposes standard operations (Observe, Search, Consolidate, Decay, Forget).

```
Agent (IAW.Core)
  |
  +-- Memory Agent
        |-- UserMemory       (user preferences, habits, corrections)
        |-- ProjectMemory    (architecture decisions, conventions, tech stack)
        |-- PatternMemory    (recurring patterns, anti-patterns, best practices)
        |-- EpisodeMemory    (task execution logs, outcomes, timelines)
        |-- CodeMemory       (file summaries, type maps, dependency graphs)
```

Each memory agent stores entries with provenance metadata, enabling trust-based retrieval and automatic decay of stale information.

## MemoryEntry

Every piece of stored knowledge is a `MemoryEntry` with provenance tracking:

```csharp
[GenerateSerializer]
public record MemoryEntry(
    [property: Id(0)] string Id,
    [property: Id(1)] string Content,
    [property: Id(2)] string Category,
    [property: Id(3)] MemorySource Source,
    [property: Id(4)] float TrustScore,
    [property: Id(5)] DateTimeOffset CreatedAt,
    [property: Id(6)] DateTimeOffset LastAccessedAt,
    [property: Id(7)] int AccessCount,
    [property: Id(8)] float[] Embedding);
```

### MemorySource

Provenance tracks where a memory came from:

```csharp
[GenerateSerializer]
public record MemorySource(
    [property: Id(0)] string AgentId,
    [property: Id(1)] string Origin,
    [property: Id(2)] DateTimeOffset Timestamp);
```

- `AgentId` -- the agent that created the memory
- `Origin` -- how it was created (e.g., `"observation"`, `"user-correction"`, `"consolidation"`)
- `Timestamp` -- when it was created

### TrustScore

The `TrustScore` (0.0 to 1.0) indicates reliability:

| Score | Meaning | Example |
|---|---|---|
| 1.0 | User-provided or verified | Direct user correction |
| 0.8 | Observed from successful outcome | Task completed without errors |
| 0.5 | Inferred by LLM | Pattern detected in code review |
| 0.3 | Stale or unverified | Old observation, never reconfirmed |

## Memory Operations

Each memory agent supports five core operations.

### Observe

Store a new memory from an observation or interaction:

```csharp
public async Task ObserveAsync(string content, string category, float trustScore = 0.8f)
{
    var embedding = await EmbeddingGenerator.GenerateEmbeddingAsync(content);
    var entry = new MemoryEntry(
        Id: Guid.NewGuid().ToString("N"),
        Content: content,
        Category: category,
        Source: new MemorySource(
            this.GetPrimaryKeyString(), "observation", DateTimeOffset.UtcNow),
        TrustScore: trustScore,
        CreatedAt: DateTimeOffset.UtcNow,
        LastAccessedAt: DateTimeOffset.UtcNow,
        AccessCount: 0,
        Embedding: embedding.Vector.ToArray());

    State[$"mem-{entry.Id}"] = new StateDescriptor($"mem-{entry.Id}",
        JsonSerializer.Serialize(entry));
    await WriteStateAsync();
}
```

### Search

Find memories semantically similar to a query:

```csharp
public async Task<IReadOnlyList<MemoryEntry>> SearchAsync(
    string query, int maxResults = 5, float minTrust = 0.3f)
{
    var queryEmbedding = await EmbeddingGenerator.GenerateEmbeddingAsync(query);
    var queryVector = queryEmbedding.Vector.ToArray();

    var entries = GetAllMemoryEntries()
        .Where(e => e.TrustScore >= minTrust)
        .Select(e => (Entry: e, Score: CosineSimilarity(queryVector, e.Embedding)))
        .OrderByDescending(x => x.Score)
        .Take(maxResults)
        .Select(x => x.Entry with
        {
            LastAccessedAt = DateTimeOffset.UtcNow,
            AccessCount = x.Entry.AccessCount + 1
        })
        .ToList();

    // Update access timestamps
    foreach (var entry in entries)
    {
        State[$"mem-{entry.Id}"] = new StateDescriptor(
            $"mem-{entry.Id}", JsonSerializer.Serialize(entry));
    }
    await WriteStateAsync();

    return entries;
}
```

### Consolidate

Merge related memories to reduce redundancy:

```csharp
public async Task ConsolidateAsync(string category)
{
    var entries = GetAllMemoryEntries()
        .Where(e => e.Category == category)
        .OrderByDescending(e => e.TrustScore)
        .ToList();

    if (entries.Count < 3) return;

    var prompt = $"""
        Consolidate these {entries.Count} memories into fewer, higher-quality entries.
        Merge duplicates, resolve contradictions (prefer higher trust scores),
        and preserve key details.

        Memories:
        {string.Join("\n", entries.Select(e => $"[{e.TrustScore:F1}] {e.Content}"))}
        """;

    var history = new List<Microsoft.Extensions.AI.ChatMessage>
    {
        new(Microsoft.Extensions.AI.ChatRole.User, prompt)
    };
    var result = await ChatClient.GetResponseAsync(history);

    // Store consolidated memory with higher trust
    await ObserveAsync(result.Text ?? "", category, trustScore: 0.9f);

    // Remove originals that were consolidated
    foreach (var entry in entries.Where(e => e.TrustScore < 0.9f))
        State.Remove($"mem-{entry.Id}");
    await WriteStateAsync();
}
```

### Decay

Reduce trust scores for memories that haven't been accessed recently:

```csharp
public async Task DecayAsync(TimeSpan threshold, float decayRate = 0.1f)
{
    var cutoff = DateTimeOffset.UtcNow - threshold;
    var staleEntries = GetAllMemoryEntries()
        .Where(e => e.LastAccessedAt < cutoff)
        .ToList();

    foreach (var entry in staleEntries)
    {
        var decayed = entry with
        {
            TrustScore = Math.Max(0.0f, entry.TrustScore - decayRate)
        };

        if (decayed.TrustScore <= 0.0f)
        {
            State.Remove($"mem-{entry.Id}");
        }
        else
        {
            State[$"mem-{entry.Id}"] = new StateDescriptor(
                $"mem-{entry.Id}", JsonSerializer.Serialize(decayed));
        }
    }
    await WriteStateAsync();
}
```

### Forget

Explicitly remove a memory:

```csharp
public async Task ForgetAsync(string memoryId)
{
    State.Remove($"mem-{memoryId}");
    await WriteStateAsync();
}
```

## Built-in Memory Agents

### UserMemory

Stores user-specific preferences, corrections, and interaction patterns. The existing `UserAgent` provides the foundation:

```csharp
var userAgent = GrainFactory.GetGrain<IUser>("user");

// Store preferences
await userAgent.SetPreferenceAsync("code-style", "prefer-expression-bodies", ct);
await userAgent.SetPreferenceAsync("review-depth", "thorough", ct);

// Store memories
await userAgent.AddMemoryAsync("User prefers dark theme in all tools", ct);
await userAgent.AddMemoryAsync("User works on IAW project primarily", ct);

// Retrieve
var style = await userAgent.GetPreferenceAsync("code-style", ct);
var memories = await userAgent.GetMemoriesAsync(ct);
```

### ProjectMemory

The `KnowledgeAgent` serves as project memory, storing architecture decisions, patterns, conventions, and tech stack:

```csharp
var knowledge = GrainFactory.GetGrain<IKnowledge>("iaw-project");

// Architecture decisions
await knowledge.AddDecision(
    "Use Orleans for agent runtime",
    "Need distributed, durable actors with streaming",
    "Orleans 10.0 with journaled grains");

// Patterns
await knowledge.AddPattern(
    "Behavior Composition",
    "Agents compose capabilities via generic interfaces",
    "IStreamConsumer<T>, IReceiver<T>, IBroadcaster<T>");

// Conventions
await knowledge.AddConvention("No XML doc comments -- use self-explanatory naming");

// Tech stack
await knowledge.SetTechStack(["Orleans 10.0", ".NET 11", "Aspire", "Roslyn"]);

// Full project summary
var summary = await knowledge.GetProjectInfo();
```

### PatternMemory

Stores recurring design patterns, anti-patterns, and best practices observed across the codebase:

```csharp
var patternMemory = GrainFactory.GetGrain<IAgent>("pattern-memory");

// Observe a pattern
var message = new ChatMessage(
    "I noticed the Singleton pattern used in LLMModel subclasses " +
    "with static readonly Instance fields", ChatRole.User);
await foreach (var r in patternMemory.SendMessage(message, ct)) { }
```

### EpisodeMemory

Records task execution episodes -- what was attempted, what succeeded, what failed, and how long it took:

```csharp
var episodeMemory = GrainFactory.GetGrain<IAgent>("episode-memory");

var message = new ChatMessage(
    "Record: Task 'upgrade-nuget-packages' completed successfully. " +
    "Duration: 45s. DotNet agent ran tests, all passed. " +
    "3 packages updated.", ChatRole.User);
await foreach (var r in episodeMemory.SendMessage(message, ct)) { }
```

### CodeMemory

Stores code-level knowledge: file summaries, type maps, dependency graphs, and API signatures. The `RoslynAgent` provides much of this data:

```csharp
var roslyn = GrainFactory.GetGrain<IRoslyn>("roslyn");
await roslyn.SetWorkspaceAsync("/src/project", ct);

// Generate and cache a type map
var typeMap = await roslyn.GetTypeMapAsync(ct);

// Analyze architecture
var architecture = await roslyn.AnalyzeArchitectureAsync(ct);

// Detect patterns
var singletons = await roslyn.DetectPatternsAsync("singleton", ct);
```

## MemoryContextProvider

The `IAIContextProvider` interface lets memory agents inject relevant context into every conversation turn automatically. When an agent has context providers registered, they are called before each LLM invocation.

```csharp
public interface IAIContextProvider
{
    Task<AIContext> ProvideContextAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    Task StoreContextAsync(
        IReadOnlyList<ChatMessage> request, AgentResponse response,
        CancellationToken ct = default);
}
```

### Implementing a Memory Context Provider

```csharp
public class MemoryContextProvider(IGrainFactory grainFactory) : IAIContextProvider
{
    public async Task<AIContext> ProvideContextAsync(
        IReadOnlyList<ChatMessage> messages, CancellationToken ct)
    {
        // Get the last user message as a search query
        var lastMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastMessage is null)
            return AIContext.Empty;

        // Search user memory for relevant context
        var userAgent = grainFactory.GetGrain<IUser>("user");
        var memories = await userAgent.GetMemoriesAsync(ct);

        if (memories.Count == 0)
            return AIContext.Empty;

        // Inject relevant memories as context
        var contextMessage = new ChatMessage(
            $"User context: {string.Join("; ", memories.Take(5))}",
            ChatRole.Assistant);

        return new AIContext([contextMessage]);
    }

    public Task StoreContextAsync(
        IReadOnlyList<ChatMessage> request, AgentResponse response, CancellationToken ct)
    {
        // Optionally store new observations from the conversation
        return Task.CompletedTask;
    }
}
```

### Registering Context Providers

Override `GetContextProviders()` in your agent to attach memory providers:

```csharp
public class SmartAssistant : Agent
{
    private readonly IGrainFactory _grainFactory;

    protected override IReadOnlyList<IAIContextProvider> GetContextProviders() =>
    [
        new MemoryContextProvider(_grainFactory)
    ];
}
```

The `AIContext` returned by providers is injected into the conversation history right after the system prompt, giving the LLM access to relevant memories without explicit user queries.

## Embedding Integration

Memory search relies on vector embeddings. IAW uses `IEmbeddingGenerator` from `Microsoft.Extensions.AI` for embedding generation. In the Aspire AppHost, configure a local embedding provider:

```csharp
// In AppHost
var silo = builder.AddProject<Projects.IAW_Silo>("silo")
    .WithReference(iaw)
    .WithLLMEnvironment(builder);

// In the silo's Program.cs, register embedding generator
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OllamaEmbeddingGenerator(new Uri("http://localhost:11434"), "nomic-embed-text"));
```

For development without a local embedding service, memory agents fall back to keyword-based search using the agent's durable state dictionary.
