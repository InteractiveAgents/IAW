# Static Virtual Interface Members for Agent Metadata — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move agent identity (DisplayName, Description, Capabilities, Instructions) from agent classes to interfaces using C# static virtual interface members, bridged via a generic `Agent<TContract>` base class.

**Architecture:** `IAgent` declares 4 static virtual properties with defaults. Each agent interface (IAspire, IShell, etc.) overrides them. A new `Agent<TContract> : Agent` bridges static interface metadata to instance properties via the generic type parameter. Agent classes drop all 4 property declarations, inheriting from `Agent<TContract>` instead of `Agent`. `AgentRegistrationStartupTask` reads metadata from interfaces via a generic helper instead of reflection hacks.

**Tech Stack:** .NET 11 preview, C# preview (static virtual interface members), Orleans 9

**Spec:** `docs/superpowers/specs/2026-03-20-static-virtual-agent-metadata-design.md`

---

### Task 1: Add static virtual properties to IAgent and create Agent<TContract>

**Files:**
- Modify: `src/Core/Contracts/IAgent.cs`
- Create: `src/Core/Agents/AgentGeneric.cs`
- Modify: `src/Core/Agents/Agent.Lifecycle.cs` (no functional change — just verify DisplayName/Instructions still work)

- [ ] **Step 1: Add static virtual properties to IAgent**

In `src/Core/Contracts/IAgent.cs`, add these 4 static virtual members to the interface:

```csharp
static virtual string AgentDisplayName => "";
static virtual string AgentDescription => "";
static virtual string[] AgentCapabilities => [];
static virtual string AgentInstructions => "You are a helpful AI assistant. Answer questions clearly and concisely.";
```

Add them right after the interface opening brace, before the existing method declarations.

- [ ] **Step 2: Create Agent<TContract> generic base class**

Create `src/Core/Agents/AgentGeneric.cs`:

```csharp
using Core.AI;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Core;

public abstract class Agent<TContract>(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent(durableState, chatClient) where TContract : IAgent
{
    protected override string DisplayName => TContract.AgentDisplayName;
    protected override string Instructions => TContract.AgentInstructions;
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build src/Core/Core.csproj`
Expected: SUCCESS — no existing code is broken, Agent<TContract> is additive.

- [ ] **Step 4: Commit**

```bash
git add src/Core/Contracts/IAgent.cs src/Core/Agents/AgentGeneric.cs
git commit -m "feat: add static virtual properties to IAgent and Agent<TContract> generic base"
```

---

### Task 2: Create AgentInterfaceMetadata helper and update registration

**Files:**
- Create: `src/Core/Registry/AgentInterfaceMetadata.cs`
- Modify: `src/Core/Registry/AgentRegistrationStartupTask.cs`

- [ ] **Step 1: Create AgentInterfaceMetadata generic helper**

Create `src/Core/Registry/AgentInterfaceMetadata.cs`:

```csharp
using System.Reflection;
using Core.Contracts;

namespace Core.Registry;

public static class AgentInterfaceMetadata
{
    public static string DisplayName<T>() where T : IAgent => T.AgentDisplayName;
    public static string Description<T>() where T : IAgent => T.AgentDescription;
    public static string[] Capabilities<T>() where T : IAgent => T.AgentCapabilities;
    public static string Instructions<T>() where T : IAgent => T.AgentInstructions;

    public static (string DisplayName, string Description, string[] Capabilities) ReadFrom(Type agentInterfaceType)
    {
        var displayName = (string)typeof(AgentInterfaceMetadata)
            .GetMethod(nameof(DisplayName))!
            .MakeGenericMethod(agentInterfaceType)
            .Invoke(null, null)!;

        var description = (string)typeof(AgentInterfaceMetadata)
            .GetMethod(nameof(Description))!
            .MakeGenericMethod(agentInterfaceType)
            .Invoke(null, null)!;

        var capabilities = (string[])typeof(AgentInterfaceMetadata)
            .GetMethod(nameof(Capabilities))!
            .MakeGenericMethod(agentInterfaceType)
            .Invoke(null, null)!;

        return (displayName, description, capabilities);
    }
}
```

- [ ] **Step 2: Update AgentRegistrationStartupTask to use the helper**

Replace the `BuildRecord` method in `src/Core/Registry/AgentRegistrationStartupTask.cs`. The full new file:

```csharp
using Core.Contracts;

namespace Core.Registry;

public class AgentRegistrationStartupTask(IGrainFactory grainFactory) : IStartupTask
{
    public async Task Execute(CancellationToken ct)
    {
        var registry = grainFactory.GetGrain<IAgentRegistry>("global");

        foreach (var agentType in DiscoverAgentTypes())
        {
            var record = BuildRecord(agentType);
            if (record is not null)
                await registry.RegisterAsync(record, ct);
        }
    }

    static IEnumerable<Type> DiscoverAgentTypes() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t is { IsAbstract: false, IsClass: true }
                && t.IsSubclassOf(typeof(IAW.Core.Agent)));

    static AgentRecord? BuildRecord(Type agentType)
    {
        var agentInterface = agentType.GetInterfaces()
            .FirstOrDefault(i => i != typeof(IAgent) && typeof(IAgent).IsAssignableFrom(i) && !i.IsGenericType);

        if (agentInterface is null)
            return null;

        var meta = AgentInterfaceMetadata.ReadFrom(agentInterface);

        var agentNamespace = ExtractNamespace(agentType);
        var displayName = meta.DisplayName.Length > 0
            ? meta.DisplayName
            : StripAgentSuffix(agentType.Name);

        return new AgentRecord
        {
            Id = Guid.NewGuid(),
            AgentType = agentType.Name,
            Namespace = agentNamespace,
            DisplayName = displayName,
            Description = meta.Description,
            Capabilities = meta.Capabilities,
            InterfaceName = agentInterface.Name
        };
    }

    static string ExtractNamespace(Type type)
    {
        var ns = type.Namespace ?? "unknown";
        var lastDot = ns.LastIndexOf('.');
        return lastDot >= 0 ? ns[(lastDot + 1)..].ToLowerInvariant() : ns.ToLowerInvariant();
    }

    static string StripAgentSuffix(string typeName)
        => typeName.EndsWith("Agent") ? typeName[..^5] : typeName;
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Core/Registry/AgentInterfaceMetadata.cs src/Core/Registry/AgentRegistrationStartupTask.cs
git commit -m "feat: replace reflection hacks in registration with AgentInterfaceMetadata generic helper"
```

---

### Task 3: Migrate LLM model interfaces and agents (13 agents)

LLM agents have the simplest pattern — no custom Instructions (they use `LlmAgentBase`'s shared instruction). Each model interface gets DisplayName, Description, Capabilities. The `LlmAgentBase` needs to become generic so `DisplayName` resolves from the interface. LLM agents override Instructions dynamically via `LlmAgentBase` using `$"You are {DisplayName}..."` — since `DisplayName` now comes from the interface via `Agent<TContract>`, this just works.

**Files:**
- Modify: `src/Core/LLM.cs` — make `LlmAgentBase` generic
- Modify: 13 model files in `src/Core/AI/Models/` — add static members to interfaces
- Modify: 13 agent files in `src/Agents/LLM/` — change base class, remove properties

- [ ] **Step 1: Make LlmAgentBase generic**

In `src/Core/LLM.cs`, change:

```csharp
// FROM:
public abstract class LlmAgentBase(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : IAW.Core.Agent(durableState, chatClient)
{
    protected override string Instructions =>
        $"You are {DisplayName}, an IAW team language model. Answer directly, accurately, and concisely.";
}

// TO:
public abstract class LlmAgentBase<TContract>(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient)
    : Agent<TContract>(durableState, chatClient) where TContract : IAgent
{
    protected override string Instructions =>
        $"You are {DisplayName}, an IAW team language model. Answer directly, accurately, and concisely.";
}
```

- [ ] **Step 2: Add static members to all 13 LLM interfaces**

Each model file in `src/Core/AI/Models/` has the interface at the bottom. Add the 3 static members (not Instructions — `LlmAgentBase` provides that). Example for `Opus46.cs`:

```csharp
// FROM:
public interface IOpus46 : IAgent { }

// TO:
public interface IOpus46 : IAgent
{
    static new string AgentDisplayName => "Claude Opus 4.6";
    static new string AgentDescription => "Claude Opus 4.6 most capable Anthropic model wrapper for complex reasoning and nuanced analysis.";
    static new string[] AgentCapabilities => ["llm", "reasoning", "generation", "claude", "anthropic", "powerful"];
}
```

Apply the same pattern to all 13 model files. The values come from the current `AgentDescription`/`AgentCapabilities` static properties and `DisplayName` override in each agent class. Full list:

| Model File | Interface | DisplayName | Description | Capabilities |
|---|---|---|---|---|
| `Claude45Haiku.cs` | `IClaude45Haiku` | `"Claude 4.5 Haiku"` | `"Claude 4.5 Haiku fast and lightweight language model wrapper optimized for low-latency tasks."` | `["llm", "reasoning", "generation", "claude", "anthropic", "fast"]` |
| `Gemini31.cs` | `IGemini31` | `"Gemini 3.1"` | `"Gemini 3.1 language model wrapper from Google for multimodal reasoning and generation."` | `["llm", "reasoning", "generation", "gemini", "google", "multimodal"]` |
| `Gpt4o.cs` | `IGpt4o` | `"GPT-4o"` | `"GPT-4o language model wrapper for multimodal reasoning and general-purpose text generation."` | `["llm", "reasoning", "generation", "openai", "multimodal"]` |
| `Gpt4oMini.cs` | `IGpt4oMini` | `"GPT-4o Mini"` | `"GPT-4o Mini compact language model wrapper balancing speed and capability for everyday tasks."` | `["llm", "reasoning", "generation", "openai", "fast"]` |
| `Gpt52.cs` | `IGpt52` | `"GPT 5.2"` | `"GPT 5.2 language model wrapper for advanced reasoning and complex task completion."` | `["llm", "reasoning", "generation", "openai"]` |
| `Gpt53.cs` | `IGpt53` | `"GPT 5.3"` | `"GPT 5.3 language model wrapper for advanced reasoning and complex task completion."` | `["llm", "reasoning", "generation", "openai"]` |
| `Gpt54Mini.cs` | `IGpt54Mini` | `"GPT-5.4 Mini"` | `"GPT-5.4 Mini compact language model wrapper offering high capability with reduced latency."` | `["llm", "reasoning", "generation", "openai", "fast"]` |
| `Gpt54Nano.cs` | `IGpt54Nano` | `"GPT-5.4 Nano"` | `"GPT-5.4 Nano ultra-lightweight language model wrapper for minimal-latency inference."` | `["llm", "reasoning", "generation", "openai", "fast", "nano"]` |
| `GrokLatest.cs` | `IGrokLatest` | `"Grok Latest"` | `"Grok Latest language model wrapper from xAI for reasoning and conversational tasks."` | `["llm", "reasoning", "generation", "grok", "xai"]` |
| `Llama32.cs` | `ILlama32` | `"Llama 3.2"` | `"Llama 3.2 open-weight language model wrapper for local and on-premise inference."` | `["llm", "reasoning", "generation", "llama", "meta", "local"]` |
| `Opus46.cs` | `IOpus46` | `"Claude Opus 4.6"` | `"Claude Opus 4.6 most capable Anthropic model wrapper for complex reasoning and nuanced analysis."` | `["llm", "reasoning", "generation", "claude", "anthropic", "powerful"]` |
| `Qwen25.cs` | `IQwen25` | `"Qwen 2.5"` | `"Qwen 2.5 language model wrapper from Alibaba for multilingual reasoning and generation."` | `["llm", "reasoning", "generation", "qwen", "alibaba", "multilingual"]` |
| `Sonnet46.cs` | `ISonnet46` | `"Claude Sonnet 4.6"` | `"Claude Sonnet 4.6 language model wrapper for general-purpose reasoning and text generation."` | `["llm", "reasoning", "generation", "claude", "anthropic"]` |

- [ ] **Step 3: Update all 13 LLM agent classes**

Each agent class in `src/Agents/LLM/`:
1. Change base class from `LlmAgentBase(...)` to `LlmAgentBase<IXxx>(...)`
2. Remove `protected override string DisplayName`
3. Remove `public static string AgentDescription`
4. Remove `public static string[] AgentCapabilities`

Example for `Opus46Agent.cs`:

```csharp
// FROM:
public class Opus46Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Opus46>] IChatClient chatClient)
    : LlmAgentBase(durableState, chatClient), IOpus46
{
    protected override string DisplayName => "Claude Opus 4.6";
    public static string AgentDescription => "Claude Opus 4.6 most capable Anthropic model wrapper for complex reasoning and nuanced analysis.";
    public static string[] AgentCapabilities => ["llm", "reasoning", "generation", "claude", "anthropic", "powerful"];
}

// TO:
public class Opus46Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Opus46>] IChatClient chatClient)
    : LlmAgentBase<IOpus46>(durableState, chatClient), IOpus46;
```

Note: when the class body is empty after removing properties, use `;` instead of `{ }`.

- [ ] **Step 4: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: SUCCESS

- [ ] **Step 5: Commit**

```bash
git add src/Core/LLM.cs src/Core/AI/Models/ src/Agents/LLM/
git commit -m "feat: migrate 13 LLM agents to static virtual interface metadata"
```

---

### Task 4: Migrate infrastructure agent interfaces and classes (4 agents)

**Files:**
- Modify: `src/Agents/Infrastructure/IAspire.cs`
- Modify: `src/Agents/Infrastructure/IFileSystem.cs`
- Modify: `src/Agents/Infrastructure/IGit.cs`
- Modify: `src/Agents/Infrastructure/IShell.cs`
- Modify: `src/Agents/Infrastructure/AspireAgent.cs`
- Modify: `src/Agents/Infrastructure/FileSystemAgent.cs`
- Modify: `src/Agents/Infrastructure/GitAgent.cs`
- Modify: `src/Agents/Infrastructure/ShellAgent.cs`

- [ ] **Step 1: Add static members to infrastructure interfaces**

For each interface, add the 4 static virtual members. Values come from the corresponding agent class. Read the full `Instructions` string from each agent class and move it to the interface.

Example for `IAspire.cs`:

```csharp
using Core.Contracts;

namespace IAW.Agents.Infrastructure;

public interface IAspire : IAgent
{
    static new string AgentDisplayName => "Aspire";

    static new string AgentDescription =>
        "Monitors and manages the running .NET Aspire application — resources, health, logs, traces, and telemetry via Aspire MCP tools.";

    static new string[] AgentCapabilities =>
        ["aspire", "health", "traces", "logs", "resources", "monitoring", "telemetry", "infrastructure", "status"];

    static new string AgentInstructions => """
        You are the Aspire infrastructure agent for the IAW system. You monitor and manage
        the running .NET Aspire application — its resources, health, logs, and traces.
        ... (move full Instructions text from AspireAgent.cs)
        """;
}
```

Apply the same pattern to `IFileSystem.cs`, `IGit.cs`, `IShell.cs` — read the full `Instructions` content from the corresponding agent class and move it.

- [ ] **Step 2: Update infrastructure agent classes**

For each agent class:
1. Change base from `Agent(durableState, chatClient)` to `Agent<IXxx>(durableState, chatClient)`
2. Remove `DisplayName`, `Instructions`, `AgentDescription`, `AgentCapabilities`
3. Keep all other behavior (tools, MCP connection, activation logic)

Example for `AspireAgent.cs` — the class keeps only its unique behavior:

```csharp
public class AspireAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    ILogger<AspireAgent> logger)
    : Agent<IAspire>(durableState, chatClient), IAspire
{
    private McpClient? _mcpClient;
    private IList<McpClientTool> _mcpTools = [];

    // ... OnActivateAsync, OnDeactivateAsync, DefineTools, ConnectMcpAsync, ResolveAppHostPath
    // (all unchanged — just remove DisplayName, Instructions, AgentDescription, AgentCapabilities)
}
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Infrastructure/
git commit -m "feat: migrate 4 infrastructure agents to static virtual interface metadata"
```

---

### Task 5: Migrate orchestration agents (3 agents)

**Files:**
- Modify: `src/Agents/Orchestration/IThread.cs`
- Modify: `src/Agents/Orchestration/IAgentSelector.cs`
- Modify: `src/Core/Contracts/ICodeOrchestrator.cs`
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`
- Modify: `src/Agents/Orchestration/AgentSelectorAgent.cs`
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`

- [ ] **Step 1: Add static members to orchestration interfaces**

`IThread.cs` and `IAgentSelector.cs` — full 4 properties (move Instructions from class).

`ICodeOrchestrator.cs` — only 3 properties (DisplayName, Description, Capabilities). Instructions stays as class override because `CodeOrchestratorAgent` builds it dynamically from the agent registry catalog.

- [ ] **Step 2: Update orchestration agent classes**

`ThreadAgent` and `AgentSelectorAgent`: change base to `Agent<IThread>` / `Agent<IAgentSelector>`, remove 4 properties.

`CodeOrchestratorAgent`: change base to `Agent<ICodeOrchestrator>`, remove `DisplayName`, `AgentDescription`, `AgentCapabilities`. **Keep** `Instructions` override (dynamic, reads from agent registry).

- [ ] **Step 3: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Orchestration/ src/Core/Contracts/ICodeOrchestrator.cs
git commit -m "feat: migrate 3 orchestration agents to static virtual interface metadata"
```

---

### Task 6: Migrate memory agents (5 agents)

Memory agents each have per-agent Instructions. `MemoryAgentBase` also provides a shared Instructions, but each agent overrides it. Make `MemoryAgentBase` generic and move metadata to interfaces.

**Files:**
- Modify: `src/Core/Memory.cs` — make `MemoryAgentBase` generic
- Modify: `src/Agents/Memory/ICodeMemory.cs`
- Modify: `src/Agents/Memory/IEpisodeMemory.cs`
- Modify: `src/Agents/Memory/IPatternMemory.cs`
- Modify: `src/Agents/Memory/IProjectMemory.cs`
- Modify: `src/Agents/Memory/IUserMemory.cs`
- Modify: 5 agent class files in `src/Agents/Memory/`

- [ ] **Step 1: Make MemoryAgentBase generic**

In `src/Core/Memory.cs`, change:

```csharp
// FROM:
public abstract class MemoryAgentBase(
    [AgentState] AgentDurableState durableState,
    IChatClient chat,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger logger)
    : Agent(durableState, chat), IMemoryAgent

// TO:
public abstract class MemoryAgentBase<TContract>(
    [AgentState] AgentDurableState durableState,
    IChatClient chat,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger logger)
    : Agent<TContract>(durableState, chat), IMemoryAgent where TContract : IMemoryAgent
```

Remove the `Instructions` override from `MemoryAgentBase` — each memory agent's interface provides its own.

- [ ] **Step 2: Add static members to memory interfaces**

Each memory interface currently extends `IMemoryAgent` (not `IAgent` directly). Since `IMemoryAgent : IAgent`, the static virtuals from `IAgent` are available. Add all 4 members.

Example for `ICodeMemory.cs`:

```csharp
using Core.Contracts;

namespace IAW.Agents.Memory;

public interface ICodeMemory : IMemoryAgent
{
    static new string AgentDisplayName => "Code Memory";

    static new string AgentDescription =>
        "Stores and retrieves code structure, dependency relationships, and implementation details via vector search.";

    static new string[] AgentCapabilities =>
        ["memory", "code", "search", "recall", "vector", "embedding"];

    static new string AgentInstructions =>
        "You are Code Memory, the IAW team's record of code structure, dependencies, and implementation details. " +
        "Track code organization, dependency relationships, and key implementation decisions.";
}
```

- [ ] **Step 3: Update memory agent classes**

Each memory agent class:
1. Change base from `MemoryAgentBase(...)` to `MemoryAgentBase<IXxx>(...)`
2. Remove `DisplayName`, `Instructions`, `AgentDescription`, `AgentCapabilities`
3. Keep `CollectionName` and all other behavior

Example for `CodeMemoryAgent.cs`:

```csharp
public class CodeMemoryAgent(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger<CodeMemoryAgent> logger)
    : MemoryAgentBase<ICodeMemory>(durableState, chatClient, memories, embedder, logger), ICodeMemory
{
    protected override string CollectionName => "iaw-code-memory";
    // ... rest of behavior unchanged
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: SUCCESS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Memory.cs src/Agents/Memory/
git commit -m "feat: migrate 5 memory agents to static virtual interface metadata"
```

---

### Task 7: Migrate knowledge agent (1 agent)

**Files:**
- Modify: `src/Agents/Knowledge/IKnowledge.cs`
- Modify: `src/Agents/Knowledge/KnowledgeAgent.cs`

- [ ] **Step 1: Add static members to IKnowledge**

Read full Instructions from `KnowledgeAgent.cs` and move to interface. Add all 4 static members.

- [ ] **Step 2: Update KnowledgeAgent**

Change base to `Agent<IKnowledge>`, remove 4 properties.

- [ ] **Step 3: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Knowledge/
git commit -m "feat: migrate knowledge agent to static virtual interface metadata"
```

---

### Task 8: Migrate C# coding agents (4 agents in Agents.CSharp)

**Files:**
- Modify: `src/Agents.CSharp/IRoslyn.cs`
- Modify: `src/Agents.CSharp/IDotNet.cs`
- Modify: `src/Agents.CSharp/INuGet.cs`
- Modify: `src/Agents.CSharp/IGitHub.cs`
- Modify: `src/Agents.CSharp/RoslynAgent.cs`
- Modify: `src/Agents.CSharp/DotNetAgent.cs`
- Modify: `src/Agents.CSharp/NuGetAgent.cs`
- Modify: `src/Agents.CSharp/GitHubAgent.cs`

- [ ] **Step 1: Add static members to C# agent interfaces**

Same pattern as infrastructure agents — all 4 properties on each interface.

- [ ] **Step 2: Update C# agent classes**

Change base to `Agent<IXxx>`, remove 4 properties. Note: `DotNetAgent` is `partial` — the base class change goes in the main file.

- [ ] **Step 3: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: SUCCESS

- [ ] **Step 4: Commit**

```bash
git add src/Agents.CSharp/
git commit -m "feat: migrate 4 C# coding agents to static virtual interface metadata"
```

---

### Task 9: Run full test suite and verify Aspire

- [ ] **Step 1: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All tests pass.

- [ ] **Step 2: Build and run Aspire**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Expected: All resources start. No activation errors in assistant logs.

- [ ] **Step 3: Verify via IAW MCP**

Use `agent_list_all` to verify all agents appear with correct DisplayName, Description, and Capabilities from their interfaces.

- [ ] **Step 4: Test AspireAgent specifically**

Use `agent_send_message` to send a message to the AspireAgent, asking it to list resources or check a trace. Verify it connects to Aspire MCP and responds with real data.

- [ ] **Step 5: Commit any fixes**

If any fixes were needed, commit them.
