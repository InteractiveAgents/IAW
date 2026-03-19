# Agent Registry, Orchestration Redesign & Generative UI

**Date:** 2026-03-19
**Status:** Draft

## Problem Statement

The current IAW architecture has several fundamental issues:

1. **Singleton grain IDs** — `client.GetGrain<IGit>("git")` means one Git agent for the entire cluster. Conversation history, memory, and state get polluted across tasks and users.
2. **God-object Project agent** — owns CompareModelsTool, Execute, task management, approvals, and conversation routing. Too many responsibilities.
3. **No intelligent agent selection** — the user (or the Project agent's LLM) must know which agents exist and manually pick them. No semantic discovery.
4. **Fragmented agent organization** — agents scattered across namespaces with no meaningful taxonomy. "Infrastructure" is a meaningless grouping.
5. **UI is hardcoded per interaction type** — separate `IClarifiable`, approvals, wizards, forms. No unified system for agents to dynamically generate UI.
6. **Duplicate discovery systems** — both `InterfaceCatalog` (static reflection) and `AgentRegistryGrain` (durable) do the same thing.

## Design Overview

This redesign introduces:

- **Namespace-based agent taxonomy** for clear categorization
- **Centralized agent registry** backed by Qdrant hybrid search
- **Two-phase agent selection**: vector search → LLM refinement
- **Dynamic grain IDs** — each task/context gets fresh agent instances
- **Semantic UI protocol** — agents emit `UIPart` lists, clients render natively
- **Thread-based conversation model** replacing the Project agent

---

## 1. Namespace Taxonomy

Flat, one level deep. The namespace IS the categorization — no description needed for an LLM to understand that `Agents.Coding` contains coding agents.

### Agents.System
Foundational primitives. Remain in `src/Agents` (not moved to Core) under the `IAW.Agents.System` namespace.

| Agent | Purpose |
|-------|---------|
| Shell | Execute shell commands |
| FileSystem | Read/write files, search code |

### Agents.Coding
The C# development team. Agents collaborate via events (e.g., `DotNetAgent` receives `CodeChangedMessage`, runs tests, publishes `tests.passed`).

| Agent | Purpose |
|-------|---------|
| DotNet | Build, test, format .NET projects, parse diagnostics (absorbs former Build agent) |
| Roslyn | Code intelligence, static analysis, architecture |
| NuGet | Package management, outdated detection |
| Git | Version control operations |
| GitHub | Repository monitoring, issues, releases |

### Agents.Models
LLM wrappers. One agent per model, each uses `[Llm<T>]` for model-specific `IChatClient` injection. Behavior and logic unchanged.

| Agent | Model |
|-------|-------|
| Sonnet46 | Claude Sonnet 4.6 |
| Opus46 | Claude Opus 4.6 |
| Claude45Haiku | Claude 4.5 Haiku |
| Gpt4o | GPT-4o |
| Gpt4oMini | GPT-4o Mini |
| Gpt52 | GPT-5.2 |
| Gpt53 | GPT-5.3 |
| Gpt54Mini | GPT-5.4 Mini |
| Gpt54Nano | GPT-5.4 Nano |
| Gemini31 | Gemini 3.1 |
| Llama32 | Llama 3.2 |
| GrokLatest | Grok Latest |
| Qwen25 | Qwen 2.5 |

### Agents.Memory
All remembering, context, and recall. Two sub-categories exist within the flat namespace:

**Vector-backed memory agents** (use Qdrant embeddings for semantic recall):

| Agent | Purpose |
|-------|---------|
| CodeMemory | Code patterns, snippets — semantic search over code |
| EpisodeMemory | Past interactions, events — temporal recall |
| PatternMemory | Recurring patterns — detect and retrieve known patterns |
| ProjectMemory | Project-specific context — per-project knowledge |

**Context provider agents** (key-value state, injected into prompts):

| Agent | Purpose |
|-------|---------|
| UserMemory | User preferences, history — vector-backed recall of user-specific knowledge |
| Knowledge | Project metadata, decisions, tech stack, conventions — structured key-value context |

Note: The former `UserAgent` (key-value preferences) is absorbed into `UserMemory`. User preferences become part of `UserMemory`'s durable state, queryable both as structured key-value lookups and via semantic search.

### Agents.Orchestration
Coordination and execution.

| Agent | Purpose |
|-------|---------|
| CodeOrchestrator | Generate & run C# orchestration apps |
| Thread | User-facing conversation, routing, task management |
| AgentSelector | Two-phase agent selection (vector search + LLM reasoning) |

### Dropped Agents
- **ReviewerAgent** — dead code, no triggers exist
- **SelfImprovementAgent** — dead code, no triggers exist
- **AspireAgent** — marker interface only, does nothing
- **Build** — merged into DotNet

---

## 2. Dynamic Agent Identity

### Problem

`client.GetGrain<IGit>("git")` creates a single shared instance. All tasks, users, and conversations share the same grain state. History gets polluted, parallel tasks conflict.

### Solution

Every agent instance gets a unique string ID. The interface type defines WHAT it is, the ID defines WHICH instance.

### ID Structure

IDs follow the convention `{scope}/{agent-type}` or are auto-generated:

```
task-2f8a/git          — Git agent scoped to a specific orchestration task
task-2f8a/dotnet       — DotNet agent for the same task
user-123/personal      — User's personal thread (persists across sessions)
sel-9x3b               — Short-lived ephemeral agent
```

### Scoping Rules

| Scope | Pattern | Lifecycle | Example |
|-------|---------|-----------|---------|
| Task-scoped | `task-{id}/{type}` | Created by orchestration, live for task duration, resumable | `task-a1b2/git` |
| User-scoped | `user-{id}/{type}` | Persist across sessions | `user-123/personal` |
| Ephemeral | `{type}-{guid}` | One-off, no reuse | `selector-8f2a3b` |

### API

```csharp
public static class ClusterClientExtensions
{
    // New instance with auto-generated ID
    public static T Get<T>(this IClusterClient client) where T : IAgent
        => client.GetGrain<T>($"{typeof(T).Name}-{Guid.NewGuid():N[..8]}");

    // Scoped instance — same scope+type = same agent (reusable)
    public static T Get<T>(this IClusterClient client, string scope) where T : IAgent
        => client.GetGrain<T>($"{scope}/{typeof(T).Name}");
}
```

IDs use the interface name directly — no transformation: `task-a1b2/IGit`, `IDotNet-3f8a2b1c`. Same pattern on `IGrainFactory` for agent-to-agent usage.

### Singletons

Some grains remain singletons with well-known IDs:

| Grain | Key | Reason |
|-------|-----|--------|
| AgentRegistry | `"global"` | One registry for the cluster |
| UserProfile | `"{userId}"` | One per user |

### What Gets Deleted

- `InterfaceCatalog.ComputeGrainId()` — no more deterministic ID computation
- All hardcoded singleton grain IDs in agent code (`"fs"`, `"git"`, `"pattern-memory"`, etc.)
- `InterfaceCatalog` as a public API — merged into AgentRegistry

---

## 3. Agent Registry

### Single Source of Truth

`AgentRegistry` replaces both `InterfaceCatalog` and `AgentRegistryGrain`. It's one global Orleans grain backed by a Qdrant collection.

### Qdrant Data Model

```csharp
public class AgentRecord
{
    [VectorStoreKey]
    public ulong Key { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string Namespace { get; set; }           // "coding", "memory", "models"

    [VectorStoreData(IsIndexed = true)]
    public string AgentType { get; set; }           // "dotnet", "roslyn", "sonnet46"

    [VectorStoreData]
    public string DisplayName { get; set; }         // "DotNet", "Roslyn"

    [VectorStoreData(IsFullTextIndexed = true)]
    public string Description { get; set; }         // natural language description

    [VectorStoreData(IsIndexed = true)]
    public List<string> Capabilities { get; set; }  // ["build", "test", "format"]

    [VectorStoreData(IsIndexed = true)]
    public string InterfaceName { get; set; }       // "IDotNet"

    [VectorStoreVector(1536, DistanceFunction.CosineSimilarity)] // 1536 = text-embedding-3-small; see Section 11 for other models
    public ReadOnlyMemory<float> DescriptionEmbedding { get; set; }
}
```

### Agent Metadata Source

Each agent class declares static metadata:

```csharp
public class DotNetAgent : Agent, IDotNet
{
    public static string AgentDescription =>
        "Builds, tests, and formats .NET projects. Runs dotnet CLI commands, "
        + "parses build diagnostics, executes test suites with filtering.";

    public static string[] AgentCapabilities => ["build", "test", "format", "diagnose"];
}
```

### Registry Lifecycle

**Startup (full sync):**
1. Reflection discovers all agent types (existing `AgentRegistrationStartupTask` pattern)
2. Read `AgentDescription` and `AgentCapabilities` from each agent class
3. Embed descriptions via `IEmbeddingGenerator`
4. Upsert all records into `agent-registry` Qdrant collection
5. Delete orphaned records (agents removed between deployments)

**Runtime:** Read-only. No agents register/deregister dynamically.

### Registry Interface

```csharp
public interface IAgentRegistry : IGrainWithStringKey
{
    // Intelligent discovery (phase 1 of agent selection)
    Task<List<AgentCandidate>> SearchAsync(string query, string? namespaceFilter = null, int top = 15);

    // Code generation support (for CodeOrchestrator)
    Task<List<AgentRecord>> GetAllAsync();
    Task<string> ToPromptStringAsync();

    // Direct lookup
    Task<AgentRecord?> GetByAgentTypeAsync(string agentType);
}
```

### Technology

- **`Microsoft.Extensions.VectorData.Abstractions`** (GA, v10.0.1) as the abstraction layer
- **`Microsoft.SemanticKernel.Connectors.Qdrant`** as the Qdrant connector
- **`HybridSearchAsync`** for combined vector + keyword + structured filtering

---

## 4. Two-Phase Agent Selection

### Phase 1 — Vector Search (narrow the field)

Embed user query → Qdrant `HybridSearchAsync` on `agent-registry` collection.

```
User: "Run my tests and fix any failing ones"
  → Vector: embed(query)
  → Keywords: ["test", "fix", "run"]
  → Filter: exclude namespace "models" (unless user asks for specific models)
  → Top 10-15 candidates with similarity scores
```

Namespace filtering is contextual — simple keyword-based rules, not another LLM call.

### Phase 2 — LLM Refinement (pick the team + plan)

`AgentSelectorAgent` receives candidates + user request. Returns:

```csharp
public record SelectionResult(
    SelectionStatus Status,                    // Ready, NeedsClarification, CannotHandle
    List<string> SelectedAgents,               // agent types to use
    List<string> SuccessCriteria,              // what "done" means
    string? Plan,                              // natural language for CodeOrchestrator
    List<ClarificationQuestion>? Questions);   // if needs clarification

public record ClarificationQuestion(
    string Text,
    List<string>? Options);                    // null = free-form, non-null = pick one

public enum SelectionStatus { Ready, NeedsClarification, CannotHandle }
```

### Three Outcomes

| Status | What happens |
|--------|-------------|
| **Ready** | Team selected, plan generated → CodeOrchestrator executes |
| **NeedsClarification** | Questions returned → Thread renders as UI options → user answers → re-run selection |
| **CannotHandle** | Thread tells user the request can't be fulfilled |

---

## 5. Thread Model

### What Is a Thread

A Thread replaces the Project agent. It's the user-facing conversation grain — one per topic/context.

Grain: `IThread`, key: `"{userId}/{slug}"`

### Built-in Threads

| Thread | Purpose |
|--------|---------|
| `{userId}/personal` | Personal assistant, general questions, daily tasks |
| `{userId}/iaw` | IAW platform monitoring, system management |

### User-Created Threads

Users create new threads via Telegram command or button. Each gets:
- A new Telegram forum topic
- A fresh `Thread` grain with isolated state
- Its own conversation history, tasks, jobs, scoped knowledge

### IThread Interface

Thread is just an Agent. Conversation, history, streaming, callbacks, scheduling — all inherited from `IAgent`. Thread has no additional interface methods:

```csharp
public interface IThread : IAgent { }
```

The Thread agent class configures its behavior through its system prompt and context providers. It manages a task board internally as durable state (exposed through conversation, not typed methods). Scheduling uses `IAgent.ScheduleJob`/`ScheduleRecurringJob` which every agent has.

### Thread Responsibilities

- **Conversation management** — history, streaming responses
- **Routing** — decides if request needs orchestration or can be answered directly
- **Task board** — AddTask, UpdateTask, ListTasks
- **Job scheduling** — ScheduleJob, CancelJob, ListJobs (Orleans reminders)
- **Recall** — Qdrant search over past results and documents
- **UI generation** — returns `AgentResponse` with semantic `UIPart` lists
- **Callback routing** — maintains `callbackId → agent` mapping, routes user interactions

### Thread Does NOT Own

- Agent selection → delegated to AgentSelector
- Code orchestration → delegated to CodeOrchestrator
- Model comparison → natural orchestration (selector picks Model agents)

### Memory Model

```
Shared (user-level)                    Per-thread
─────────────────                      ──────────
UserMemory: preferences, style         Thread conversation history
EpisodeMemory: past interactions       Thread tasks & jobs
                                       Thread-scoped knowledge
                                       (what this thread is about,
                                        codebase context, past decisions)
```

When switching threads:
- Assistant knows the user (shared UserMemory)
- Doesn't carry IAW context into personal thread (thread-scoped)
- Each thread's conversation history is isolated

### Notifications & Scheduled Jobs

No separate "scheduled" or "notifications" topics. Job results and notifications route to the thread that created them.

---

## 6. Generative UI Protocol

### Problem

Current system has separate interfaces for each UI interaction: approvals, wizards, forms, paginators, menus. Adding new interaction types requires new interfaces, new callback handlers, new widget state management.

### Solution: Semantic UIParts

Agents emit `UIPart` lists. Each client renders them natively. One `HandleCallback` method for all user interactions.

### UIPart Types

```csharp
[GenerateSerializer]
public abstract record UIPart;

public record TextPart(string Content, TextStyle Style = TextStyle.Normal) : UIPart;

public record OptionsPart(string Prompt, List<Option> Options,
    string CallbackId, bool AllowMultiple = false) : UIPart;

public record Option(string Label, string Value, string? Description = null);

public record CardPart(string? Title, List<CardField> Fields,
    string? ImageUrl = null) : UIPart;

public record CardField(string Label, string Value);

public record MediaPart(string Url, string FileName, string MimeType,
    string? Caption = null) : UIPart;

public record ProgressPart(string Message, double? Percent = null) : UIPart;

public record FormPart(string CallbackId, string Prompt,
    List<FormField> Fields) : UIPart;

public record FormField(string Id, string Label, FormFieldType Type,
    List<Option>? Options = null);

public enum TextStyle { Normal, Success, Warning, Error, Muted }
public enum FormFieldType { Text, SingleChoice, MultiChoice, Date, Number }
```

### Agent Response

```csharp
public record AgentResponse(List<UIPart> Parts);
```

### Rendering Per Client

DevUI (Blazor) is out of scope for this spec. It can be added later as a third renderer.

| UIPart | Telegram | MCP |
|--------|----------|-----|
| TextPart | HTML-formatted message | Plain text in JSON |
| OptionsPart | InlineKeyboardMarkup | Structured JSON with options |
| CardPart | Formatted text + photo | JSON object |
| MediaPart | sendPhoto / sendDocument | File URL in JSON |
| ProgressPart | Edit message with status | Status text |
| FormPart | Multi-step wizard (buttons) | Sequential prompts |

### Callback — One Method

```csharp
// On IAgent base interface
Task<AgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct);
```

Works for approvals, clarifications, form submissions, option selections. The agent decides what happens based on the `callbackId` it generated.

### Callback Routing

When an agent emits a `UIPart` with a `callbackId`, the **Thread grain** registers the mapping automatically:

1. Thread calls an agent (or itself) and gets back an `AgentResponse`
2. Thread scans response for any `UIPart` with a `callbackId` (OptionsPart, FormPart)
3. Thread stores `callbackId → (sourceGrainType, sourceGrainId, expiresAt)` in its durable state
4. Thread forwards the response to the UI client (Telegram/MCP)
5. When a callback arrives, Thread looks up the mapping and calls `HandleCallback` on the correct grain

```csharp
// In Thread grain
private async Task RegisterCallbacks(AgentResponse response, string sourceGrainType, string sourceGrainId)
{
    foreach (var part in response.Parts)
    {
        if (part is OptionsPart opt)
            durableState.Callbacks[opt.CallbackId] = new(sourceGrainType, sourceGrainId, DateTimeOffset.UtcNow.AddMinutes(30));
        else if (part is FormPart form)
            durableState.Callbacks[form.CallbackId] = new(sourceGrainType, sourceGrainId, DateTimeOffset.UtcNow.AddMinutes(60));
    }
}
```

The Telegram client does NOT route callbacks directly to agents — it always goes through the Thread grain, which owns the routing table. This keeps callback state co-located with conversation state.

### UISession Simplification

UISession no longer needs separate dictionaries for approvals, wizards, forms, menus. The Thread grain owns callback routing. UISession is reduced to minimal Telegram-specific state (chat IDs, topic IDs) or potentially eliminated entirely.

---

## 7. Full Request Lifecycle

```
User: "Run my tests and if anything fails, fix it"
              │
              ▼
     ┌────────────────┐
     │  Thread         │  (user-scoped: "user-123/iaw")
     │  Decides: needs │  orchestration
     │  orchestration  │
     └───────┬────────┘
              │
              ▼
     ┌────────────────┐
     │  AgentRegistry  │  (global singleton)
     │  HybridSearch   │  → DotNet(0.92), Roslyn(0.85),
     │                 │    FileSystem(0.78), Shell(0.71)
     └───────┬────────┘
              │
              ▼
     ┌────────────────┐
     │  AgentSelector  │  (ephemeral)
     │  LLM reasoning  │  Picks: DotNet, Roslyn, FileSystem
     │                 │  Criteria: "All tests pass"
     │                 │  Plan: "1. Run tests 2. Analyze 3. Fix 4. Re-run"
     └───────┬────────┘
              │  status: Ready
              ▼
     ┌────────────────┐
     │ CodeOrchestrator│  (task-scoped: "task-a1b2")
     │ Generates C# app│
     │ Creates agents: │
     │   client.Get<IDotNet>("task-a1b2")
     │   client.Get<IRoslyn>("task-a1b2")
     │   client.Get<IFileSystem>("task-a1b2")
     └───────┬────────┘
              │  Agents collaborate with isolated state
              │  Progress events → Telegram ProgressPart
              ▼
     ┌────────────────┐
     │  Thread         │  Evaluates result vs success criteria
     │  Returns:       │  AgentResponse([
     │                 │    TextPart("Fixed 2 tests", Success),
     │                 │    CardPart("Results", [...])
     │                 │  ])
     └────────────────┘
              │
              ▼
     Telegram renders: formatted message + card
```

### Clarification Flow

```
AgentSelector returns NeedsClarification
              │
              ▼
Thread returns AgentResponse([
  OptionsPart("Which test project?",
    ["Core.Tests", "Integration.Tests", "All"],
    callbackId: "clarify-8f2x")
])
              │
              ▼
Telegram renders inline keyboard buttons
User clicks "Core.Tests"
              │
              ▼
Thread.HandleCallback("clarify-8f2x", "Core.Tests")
Re-runs AgentSelector with enriched context
Flow continues → Ready → Orchestrate
```

---

## 8. Agent Communication Model

Three communication mechanisms, three coupling levels, three use cases. This is the core of how agents interact.

| | `IReceiver<T>` | Streams (`IStreamConsumer<T>`) | Observers (`IGrainObserver`) |
|---|---|---|---|
| **Direction** | Sender → specific receiver | Publisher → any subscriber | Grain → subscribed watchers |
| **Coupling** | Tight — sender knows receiver | Loose — publisher doesn't know subscribers | Medium — grain holds observer refs |
| **Typing** | Strongly typed messages | Strongly typed events | Custom observer interface |
| **Who initiates** | Sender pushes | Subscriber subscribes to stream | Watcher subscribes to grain |
| **Delivery** | Synchronous, awaitable | Async, fire-and-forget | Direct callback, in-memory |
| **Persistence** | No | Depends on stream provider | No |
| **Use case** | "Hey DotNet, code changed — react" | "Code changed event happened, anyone who cares" | "Telegram client watching for real-time updates" |

### How They Work Together

```
1. Git agent commits code
   └── IReceiver: sends CodeChangedMessage directly to DotNet agent
       (tight coupling — Git KNOWS DotNet needs to react)

2. DotNet runs tests, publishes result
   └── Stream: publishes TestsPassedEvent to stream
       (loose coupling — DotNet doesn't know who cares)

3. Telegram client watching the thread
   └── Observer: subscribed to Thread grain, gets real-time
       UI updates pushed instantly (no polling, no stream overhead)
```

### Agent Base Class Partials

| Partial file | Mechanism | Purpose |
|---|---|---|
| `Agent.Streams.cs` | Streams | Auto-subscribe to streams based on `IStreamConsumer<T>` interfaces |
| `Agent.Observers.cs` | Observers | Subscribe/unsubscribe watchers, push notifications to them |
| `Agent.Scheduling.cs` | Durable Jobs | Schedule/cancel jobs, receive and process fired jobs |

Note: `IReceiver<T>` is implemented directly on the agent's grain interface (e.g., `IDotNet : IAgent, IReceiver<CodeChangedMessage>`) — no partial file needed.

Note: This table and explanation must be added to the website documentation (`website/guide/communication.md`) as it defines the core communication architecture.

---

## 9. Tools Architecture

### Two Sources of Tools

Every agent has tools from two sources, both available to its LLM during `GetResponse()`:

**1. Interface methods (auto-discovered)**

The base `Agent` class reflects on the grain interface and registers every method (beyond `IAgent` base methods) as an AI tool automatically:

```csharp
public interface IDotNet : IAgent
{
    Task<BuildResult> BuildAsync(string project, CancellationToken ct);
    Task<TestResult> TestAsync(string? filter, CancellationToken ct);
    Task FormatAsync(CancellationToken ct);
}
```

These methods are:
- Callable directly from code: `await dotnet.BuildAsync("Core", ct)` — typed, zero tokens
- Available to the agent's LLM as tools: "BuildAsync", "TestAsync", "FormatAsync"
- Available in generated orchestration code as typed calls

The base `Agent` class auto-discovers them:

```csharp
// In Agent base class (automatic, no manual DefineTools override needed)
protected virtual IEnumerable<AIFunction> DiscoverInterfaceTools()
{
    // Reflect on the concrete grain interface (IDotNet, IGit, etc.)
    // Each method beyond IAgent becomes an AI tool
    // Method name = tool name, parameters = tool parameters
    // [Description] attributes provide tool descriptions for the LLM
}
```

**2. External tools (MCP, custom)**

For tools that come from external sources (MCP servers, dynamic tools), agents override `DefineAdditionalTools()`:

```csharp
public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    IMcpClient aspireMcp)
    : Agent(durableState, chatClient), IAspire
{
    // IAspire interface methods → auto-registered as tools

    // MCP tools from Aspire server → registered via override
    protected override IEnumerable<AIFunction> DefineAdditionalTools()
        => aspireMcp.GetTools();
}
```

### Tool Resolution for LLM

When the agent's LLM processes a `GetResponse()` call, it sees all tools from both sources in a single flat list. It doesn't know or care where a tool came from.

### Calling Patterns Summary

| Caller | Path | Tokens | Example |
|--------|------|--------|---------|
| Code (orchestration) | Typed interface method | Zero | `await dotnet.BuildAsync("Core", ct)` |
| Code (ambiguous request) | `GetResponse()` → LLM reasons → calls tools | Yes | `await dotnet.GetResponse("build and check for warnings")` |
| LLM (during reasoning) | Calls interface method as AI tool | N/A (already in LLM context) | LLM calls "BuildAsync" tool |
| LLM (external capability) | Calls MCP tool | N/A | LLM calls "aspire_list_resources" tool |

### What Changes From Today

- `DefineTools()` manual overrides are **deleted** from most agents — interface methods auto-register
- `DefineAdditionalTools()` replaces `DefineTools()` for agents that need MCP/external tools
- Agents that had tools duplicating their interface methods (most of them) get simpler — just the interface

---

## 9. CodeOrchestrator Changes

### Dynamic IDs in Generated Code

Old generated code:
```csharp
var git = client.GetGrain<IGit>("git");
```

New generated code:
```csharp
var taskId = $"task-{Guid.NewGuid():N[..8]}";
var git = client.Get<IGit>(taskId);
var dotnet = client.Get<IDotNet>(taskId);
// Each agent has isolated state for this orchestration
```

### Instructions Template

The orchestrator's system prompt uses `AgentRegistry.ToPromptStringAsync()` instead of the deleted `InterfaceCatalog.ToPromptString()`. Agent catalog is grouped by namespace:

```
[coding]
- IDotNet: builds, tests, formats .NET projects
  client.Get<IDotNet>(taskId)
- IRoslyn: code intelligence, static analysis
  client.Get<IRoslyn>(taskId)

[memory]
- ICodeMemory: code patterns and snippets
  client.Get<ICodeMemory>(taskId)
```

---

## 10. What Gets Deleted

| Component | Reason |
|-----------|--------|
| `InterfaceCatalog` (public API) | Merged into AgentRegistry |
| `InterfaceCatalog.ComputeGrainId()` | Dynamic IDs, no deterministic computation |
| `CompareModelsTool` | Natural orchestration via AgentSelector |
| `Project` agent | Replaced by Thread |
| `ReviewerAgent` | Dead code |
| `SelfImprovementAgent` | Dead code |
| `AspireAgent` | Marker interface, does nothing |
| `BuildAgent` | Merged into DotNet |
| `DefineTools()` manual overrides (most agents) | Interface methods auto-register as tools |
| Separate approval/wizard/form interfaces | Replaced by UIPart + HandleCallback on IAgent |
| Separate "scheduled"/"notifications" topics | Events route to originating thread |

### IAgent Additions

```csharp
public interface IAgent : IGrainWithStringKey
{
    // Existing — conversation
    Task<string> GetResponse(string prompt, CancellationToken ct);
    IAsyncEnumerable<string> GetResponseStream(ChatMessage message, CancellationToken ct);
    Task<List<ChatMessage>> GetHistory(CancellationToken ct);
    Task ClearHistory(CancellationToken ct);
    Task<AgentMetadata> GetMetadata(CancellationToken ct);
    Task<AgentCapabilities> GetCapabilities(CancellationToken ct);
    Task<TokenUsage?> GetLastUsage(CancellationToken ct);

    // New — UI callbacks (any agent can emit UIParts and handle responses)
    Task<AgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct);

    // New — scheduling via Durable Jobs v2 (replaces Reminders v1)
    Task ScheduleJob(string name, TimeSpan delay, string prompt, CancellationToken ct);
    Task ScheduleRecurringJob(string name, TimeSpan interval, string prompt, CancellationToken ct);
    Task CancelJob(string name, CancellationToken ct);
    Task<List<ScheduledJobInfo>> ListJobs(CancellationToken ct);
}
```

### Agent Base Class Partials (updated)

| Partial file | Responsibility |
|---|---|
| `Agent.cs` | Core: activation, LLM streaming, response handling, context enrichment |
| `Agent.Events.cs` | Typed event publishing to Orleans streams |
| `Agent.Lifecycle.cs` | Activation hooks, deactivation |
| `Agent.State.cs` | Durable state (history, key-value dict, event log) |
| `Agent.Streams.cs` | Auto-subscribe to streams based on `IStreamConsumer<T>` interfaces |
| `Agent.Tools.cs` | Auto-discovery of interface methods as AI tools + additional tools |
| `Agent.Scheduling.cs` | Durable Jobs v2: schedule/cancel/receive jobs (replaces `Agent.Tracking.cs`) |
| `Agent.Observers.cs` | Observer pattern for real-time push to watchers |

**Deleted partials:**
- `Agent.Tracking.cs` — replaced by `Agent.Scheduling.cs` (Durable Jobs v2 instead of Reminders v1)

## 11. AgentSelector Model Strategy

The `AgentSelectorAgent` uses the **default model** (first in the `WithLLM<T>()` chain, no `[Llm<T>]` attribute). This is intentional — the selector's job is lightweight reasoning over a small candidate list (10-15 agents), not heavy generation. The default model is sufficient.

If selection quality becomes a bottleneck, the selector can be upgraded to a specific model via `[Llm<T>]` without changing the architecture.

---

## 12. Embedding Dimension

The `AgentRecord.DescriptionEmbedding` dimension depends on the embedding model configured via `IEmbeddingGenerator`. The `[VectorStoreVector]` attribute dimension must match the deployed model:

| Model | Dimension |
|-------|-----------|
| OpenAI text-embedding-3-small | 1536 |
| OpenAI text-embedding-3-large | 3072 |
| Ollama nomic-embed-text | 768 |

The dimension is set at Qdrant collection creation time. If the embedding model changes, the collection must be recreated. The startup task should validate the dimension matches.

---

## 13. Dynamic Agent Cleanup

### Problem

Dynamic IDs mean agent instances accumulate durable state in storage. Orleans grain deactivation frees memory but does NOT delete persisted state.

### Cleanup Strategy

| Scope | Cleanup |
|-------|---------|
| Task-scoped (`task-{id}/*`) | Cleaned up after orchestration completes. CodeOrchestrator calls `ClearState()` on all task-scoped agents after collecting results. |
| Ephemeral (`{type}-{guid}`) | State cleared on deactivation. These agents override `OnDeactivateAsync` to call `ClearStateAsync()`. |
| User-scoped (`user-{id}/*`) | Never cleaned up automatically. These persist across sessions (threads, user memory). |

Additionally, a periodic **cleanup reminder** on the AgentRegistry grain scans for orphaned task-scoped state older than 24 hours and deletes it.

---

## 14. Migration Strategy

### State Migration

| Current State | Action |
|---------------|--------|
| Project grain state (`{userId}/general`, `{userId}/personal`) | Migrate to Thread grain state with matching keys. Conversation history, tasks, and jobs carry over. |
| Memory agent state (singleton `"code-memory"`, `"user-memory"`, etc.) | Keep under existing keys. Memory agents become user-scoped (`user-{id}/code-memory`) — migrate state from singleton keys to user-scoped keys on first access. |
| UserProfile state | No change — already keyed by userId. |
| LLM agent state (singleton `"sonnet46"`) | Discard. These are ephemeral conversations with no long-term value. |
| Infrastructure agent state (singleton `"git"`, `"shell"`) | Discard. Command execution state is ephemeral. |

### Code Migration

1. Delete `InterfaceCatalog` public API, move reflection logic into `AgentRegistrationStartupTask`
2. Replace all `GetGrain<T>("singleton-id")` calls with `Get<T>(scope)` or `Get<T>()`
3. Rename `Project` → `Thread`, update `IProject` → `IThread`, update grain type constant
4. Delete `CompareModelsTool`, `BuildAgent`, `ReviewerAgent`, `SelfImprovementAgent`, `AspireAgent`
5. Move agents into new namespace structure
6. Update CodeOrchestrator template to use `Get<T>(taskId)` pattern
7. Update MCP tools (see below)
8. Update Telegram client for new Thread keys and removed topics

### Telegram Migration

| Current Topic | New State |
|---------------|-----------|
| `personal` | Maps to Thread `{userId}/personal` (built-in) |
| `iaw` | Maps to Thread `{userId}/iaw` (built-in) |
| `scheduled` | Deleted. Existing scheduled jobs migrate to the thread that would logically own them. |
| `notifications` | Deleted. Notifications route to the originating thread. |
| No-topic messages | Route to `{userId}/personal` as the default thread. |

---

## 15. MCP Server Updates

The MCP server (`src/IAW.MCP`) must adapt to the new architecture:

### Tool Changes

| Current Tool | Change |
|-------------|--------|
| `assistant_chat` | Routes to Thread grain instead of resolving via InterfaceCatalog. Parameter `projectId` renamed to `threadSlug`. Default: `"personal"`. |
| `agent_list_all` | Uses `AgentRegistry.GetAllAsync()` instead of `InterfaceCatalog.Discover()`. Returns namespace-grouped agent types (not instances). |
| `agent_send_message` | Creates ephemeral agent instance via `Get<T>()` instead of using singleton ID. |
| `agent_get_status` | Requires a full grain ID (not just type), since agents are no longer singletons. |
| `agent_trigger_self_improvement` | Deleted (SelfImprovementAgent dropped). |

### New MCP Capability

MCP responses can include structured `UIPart` data in JSON format, enabling MCP clients (like Claude Code) to render options and cards if they choose.

---

## 16. Technology Choices

| Concern | Technology |
|---------|-----------|
| Vector search | Qdrant (already wired via `WithVectorDb()`) |
| Vector abstraction | `Microsoft.Extensions.VectorData.Abstractions` (GA v10.0.1) |
| Qdrant connector | `Microsoft.Extensions.VectorData.Qdrant` (standalone, no SemanticKernel dependency) |
| Hybrid search | `IKeywordHybridSearch<T>.HybridSearchAsync()` on the Qdrant connector |
| Embeddings | `IEmbeddingGenerator` (already available, model-dependent dimension) |
| Grain identity | `IGrainWithStringKey` (Microsoft recommended default) |
| Serialization | Orleans `[GenerateSerializer]` for all UIPart types |
