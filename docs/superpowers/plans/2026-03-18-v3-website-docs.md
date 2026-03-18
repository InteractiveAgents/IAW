# Website Documentation Update Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update website docs to reflect current codebase and add high-quality real-world code examples.

**Architecture:** Fix 3 outdated pages (naming issues + tutorial), add concise code examples showing reactive agents, self-diagnostics, context enrichment, and pub/sub patterns.

**Tech Stack:** VitePress, Markdown

**Spec:** `docs/superpowers/specs/2026-03-18-v3-cleanup-design.md` (Sub-project 2)

---

### Task 1: Fix outdated base class names

**Files:**
- Modify: `website/guide/llm-agents.md`
- Modify: `website/guide/memory.md`

- [ ] **Step 1: Fix llm-agents.md**

Replace all references to `LLM` base class with `LlmAgentBase`. This includes class declarations, inheritance examples, and any prose mentioning "the LLM base class".

- [ ] **Step 2: Fix memory.md**

Replace all references to `Memory` base class with `MemoryAgentBase`. Same treatment.

- [ ] **Step 3: Commit**

```bash
git add website/guide/llm-agents.md website/guide/memory.md
git commit -m "docs: fix LLM→LlmAgentBase and Memory→MemoryAgentBase in website"
```

---

### Task 2: Fix first-agent tutorial

**Files:**
- Modify: `website/tutorials/first-agent.md`

The tutorial uses the old 5-parameter constructor and `Core.V3` namespace. Update to match current API.

- [ ] **Step 1: Update the tutorial**

Key changes:
- Replace old constructor `(state, eventLog, chatClient, history, trackingItems)` with current: `([AgentState] AgentDurableState durableState, [Llm<Claude45Haiku>] IChatClient chatClient)`
- Replace `Core.V3` namespace references with `Core`, `Core.Contracts`, `IAW.Core`
- Replace `GetMetadataAsync` with `GetMetadata` (sync return)
- Replace `GetEventLogAsync` with `GetEventLog`
- Ensure the example agent compiles against current API — it should inherit from `Agent`, override `Instructions` and `DisplayName`, optionally override `DefineTools()`
- Keep it simple: a minimal working agent in ~15 lines

Example of what the tutorial agent should look like:
```csharp
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

public class WeatherAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent(durableState, chatClient), IWeatherAgent
{
    protected override string DisplayName => "Weather";
    protected override string Instructions =>
        "You are a weather assistant. Provide current weather information.";
}

public interface IWeatherAgent : IAgent { }
```

- [ ] **Step 2: Verify no old API references remain**

Search the file for `Core.V3`, `GetMetadataAsync`, `GetEventLogAsync`, old constructor patterns.

- [ ] **Step 3: Commit**

```bash
git add website/tutorials/first-agent.md
git commit -m "docs: update first-agent tutorial to current API"
```

---

### Task 3: Add real-world code examples page

**Files:**
- Create: `website/guide/examples.md`
- Modify: `website/.vitepress/config.ts` or `website/.vitepress/config.mts` — add "Examples" to sidebar

- [ ] **Step 1: Create examples.md**

Create `website/guide/examples.md` with these 4 examples. Each must be concise, self-contained, and demonstrate a real pattern:

**Example 1 — Reactive fan-out: multiple agents react to same event**

Show how `IReceiver<T>` enables parallel reactive processing. Two agents independently react to `CodeChangedMessage` — one formats, one tests. No orchestration code needed.

```csharp
public class FormatterAgent : Agent, IReceiver<CodeChangedMessage>
{
    public async Task<MessageReceipt> ReceiveAsync(CodeChangedMessage msg, CancellationToken ct)
    {
        await GetResponse($"Run dotnet format on {msg.FilePath}", ct);
        return MessageReceipt.Accepted();
    }
}

public class TestAgent : Agent, IReceiver<CodeChangedMessage>
{
    public async Task<MessageReceipt> ReceiveAsync(CodeChangedMessage msg, CancellationToken ct)
    {
        await GetResponse($"Run tests for {msg.ProjectPath}", ct);
        return MessageReceipt.Accepted();
    }
}
```

**Example 2 — Self-diagnostics: system monitors itself**

Show how scheduled jobs + specialized agents create self-monitoring. One line schedules it, the AspireAgent handles the rest.

```csharp
await project.ScheduleJob("System Health", TimeSpan.FromMinutes(5),
    "Check all Aspire resources. Report only unhealthy services.", ct);
```

**Example 3 — Context enrichment: automatic memory injection**

Show how every LLM call gets enriched with relevant context — user preferences, project state, past documents — without manual prompt engineering.

```csharp
protected override IReadOnlyList<IAgentContextProvider> GetContextProviders() =>
[
    new UserContextProvider(GrainFactory),
    new ProjectContextProvider(durableState.Tasks, durableState.Files, durableState.ProjectMeta),
    new RAGContextProvider(qdrant, embeddings)
];
```

**Example 4 — Pub/sub: stream-based event distribution**

Show typed event publishing and auto-subscribed consumers.

```csharp
// Producer
await PublishToStream(new BuildCompletedEvent(ProjectPath, Success: true));

// Consumer — auto-subscribed via interface declaration
public class DeployAgent : Agent, IStreamConsumer<BuildCompletedEvent>
{
    public Task OnStreamEventAsync(BuildCompletedEvent evt, StreamSequenceToken? token) =>
        GetResponse($"Deploy {evt.ProjectPath} — build {(evt.Success ? "green" : "red")}", default);
}
```

Frame each example with a short paragraph explaining the pattern and when to use it. No boilerplate, no fluff.

- [ ] **Step 2: Add to sidebar**

Find the VitePress config file and add an "Examples" entry to the Guide section of the sidebar, linking to `/guide/examples`.

- [ ] **Step 3: Commit**

```bash
git add website/guide/examples.md website/.vitepress/
git commit -m "docs: add real-world code examples — reactive agents, self-diagnostics, context, pub/sub"
```

---

### Task 4: Delete demo directory and push

- [ ] **Step 1: Delete demo/**

```bash
rm -rf demo/
```

- [ ] **Step 2: Final push**

```bash
git add -A
git commit -m "chore: delete demo directory"
git push origin v3
```
