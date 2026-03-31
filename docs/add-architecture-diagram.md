# Task: Add Interactive Architecture Diagram to the IAW VitePress Website

## Goal

Implement an interactive SVG architecture diagram on `website/guide/architecture.md`,
modelled after https://hex1b.dev/guide/#architecture — hover any component to see its
description in a panel below, click to lock/unlock. The diagram must reflect the
**actual current codebase on master**, not docs or guesses.

---

## Step 1 — Read the codebase before writing anything

Work through these files in order. Do not skip any section.

### 1.1 Agent base class (understand what every agent inherits)

```
src/Core/Agents/Agent.cs
src/Core/Agents/Agent.Tools.cs        ← GetAllTools(), DiscoverInterfaceTools(), WorkspaceTools, SchedulingTools
src/Core/Agents/Agent.Scheduling.cs  ← IDurableJobHandler, ILocalDurableJobManager, ScheduledJobItem
src/Core/Agents/Agent.Events.cs
src/Core/Agents/Agent.Streams.cs
src/Core/Agents/Agent.State.cs       ← AgentDurableState fields: History, State, EventLog, ScheduledJobs
src/Core/Agents/Agent.Lifecycle.cs
src/Core/Agents/Agent.Observers.cs
```

Key things to confirm:
- Constructor params: `[AgentState] AgentDurableState`, `IChatClient` (optionally `[Llm<T>]`)
- `DurableChatHistoryProvider` with `ChatReducer` + `HistorySummarizer`
- `DiscoverInterfaceTools()` auto-registers public interface methods as AI tools
- Scheduling uses `Orleans.DurableJobs` (not Orleans Reminders)

### 1.2 Orchestration flow (the critical path a user message takes)

```
src/Agents/Orchestration/ThreadAgent.cs          ← entry point for all user messages
src/Agents/Orchestration/AgentSelectorAgent.cs   ← LLM-based agent picker
src/Agents/Orchestration/CodeOrchestratorAgent.cs
src/Agents/Orchestration/TelegramUIAgent.cs      ← formatting-only agent, no history
src/Core/Orchestration/ScriptGenerator.cs
src/Agents.CSharp/OrchestrationCompiler.cs
```

Understand the actual branching logic:
- `ThreadAgent` has two tools: `SendToAgent` (single agent) and `Orchestrate` (multi-agent)
- `ExecuteDelegation` calls `AgentSelectorAgent.SelectAsync()` → `SelectionResult` with
  status `Ready | NeedsClarification | CannotHandle`
- `Ready + 1 agent` → direct `IAgent.GetResponse()` call
- `Ready + N agents` → `CodeOrchestratorAgent.ExecuteCodeOrchestration()`
- Code orchestration: `ScriptGenerator` generates C# → `OrchestrationCompiler` (Roslyn)
  validates → out-of-process `dotnet run`

### 1.3 All agent implementations — read directory listings, then key files

```
src/Agents/Infrastructure/    ← Shell, FileSystem, Git, Aspire, IAWSystem
src/Agents/LLM/               ← all model wrapper agents (14 agents — list filenames)
src/Agents/Memory/            ← UserMemory, ProjectMemory, PatternMemory, EpisodeMemory, CodeMemory
src/Agents/Knowledge/         ← KnowledgeAgent
src/Agents/UserProfile/       ← UserProfile
src/Agents.CSharp/Roslyn/     ← RoslynAgent
src/Agents.CSharp/DotNet/     ← DotNetAgent
src/Agents.CSharp/GitHub/     ← GitHubAgent
src/Agents.CSharp/NuGet/      ← NuGetAgent
```

For each group note: what does the agent actually do (read Instructions string or class
summary), what tools it exposes, which LLM model it uses (`[Llm<T>]`).

### 1.4 Context providers (how prompts get enriched before LLM calls)

```
src/Core/Context/              ← IAgentContextProvider interface + all implementations
```

`ThreadAgent` wires: `UserContextProvider`, `RAGContextProvider` (Qdrant), `MemoryContextProvider`.
Note what each injects and when it's conditionally added.

### 1.5 Entry points

```
src/Clients.Telegram/TelegramBotService.cs   ← voice transcription, streaming, forum topics,
                                                /newchat, /cleanup, callback queries, TelegramUIAgent formatting
src/IAW.MCP/                                 ← MCP server on :5300, what tools it exposes
src/DevUI/                                   ← Blazor app, what agent it connects to
```

### 1.6 Registry & observability

```
src/Core/Registry/AgentRegistryGrain.cs              ← SearchAsync, GetAllAsync, ToPromptStringAsync
src/Core/Registry/AgentRegistrationStartupTask.cs    ← DiscoverAndBuildRecords() at silo startup
src/Core/Observability/AgentTelemetry.cs             ← ActivitySource "IAW", metrics, counters
```

### 1.7 Infrastructure (how the silo is composed)

```
src/IAW.AppHost/               ← Aspire topology: AddIAW(), WithLLM<T>(), WithOllama(),
                                  WithStorage(), WithVectorDb()
src/IAW.Assistant/             ← production silo (single builder.AddIAW())
src/Aspire.Hosting.IAW/        ← AppHost extension API
src/Aspire.IAW.Client/         ← service/client registration, OTel setup
```

### 1.8 Existing website — understand what's already there

```
website/.vitepress/theme/index.ts           ← existing component registrations
website/.vitepress/theme/BehaviorTabs.vue   ← style reference for existing Vue components
website/.vitepress/theme/custom.css         ← CSS custom properties available
website/guide/architecture.md              ← the page to augment; keep all existing text,
                                              place <ArchitectureDiagram /> at the top
                                              below the intro paragraph
```

---

## Step 2 — Build the Vue component

Create `website/.vitepress/theme/components/ArchitectureDiagram.vue`.

### Layout (5 horizontal rows)

```
┌─────────────────────────────────────────────────────────┐
│  ROW 1 — Entry Points                                    │
│  [Telegram Bot]    [MCP Server :5300]    [DevUI Blazor]  │
└────────────┬──────────────┬──────────────────┬──────────┘
             │              │                  │
             └──────────────▼──────────────────┘
┌─────────────────────────────────────────────────────────┐
│  ROW 2 — Orchestration                                   │
│           [ThreadAgent (Orleans Grain)]                  │
│       context: User · RAG · Memory                      │
└───────────┬──────────────────────┬──────────────────────┘
            │                      │
   SendToAgent (1 agent)      Orchestrate (N agents)
            │                      │
            ▼                      ▼
┌────────────────┐   ┌─────────────────────────────────┐
│  ROW 3a        │   │  ROW 3b                          │
│  Direct call   │   │  AgentSelector → CodeOrchestrator│
│  IAgent        │   │  ScriptGenerator → Roslyn compile│
│  .GetResponse()│   │  → out-of-process dotnet run     │
└────────────────┘   └─────────────────────────────────┘
             │                      │
             └──────────────┬───────┘
┌─────────────────────────────────────────────────────────┐
│  ROW 4 — Agent Cluster (Orleans Silo)  · dashed border  │
│  [Infrastructure]  [CSharp]  [Memory]  [LLM Wrappers]  │
│  Shell/FS/Git/     Roslyn/   5 memory  14 model agents  │
│  Aspire/IAWSystem  DotNet/   agents    (Sonnet, GPT…)   │
│                    GitHub/             [Knowledge]       │
│                    NuGet               [UserProfile]     │
└─────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────┐
│  ROW 5 — Infrastructure                                  │
│  [Aspire + OTel]  [LLM Providers]  [Durable State]     │
│                   OpenAI/Anthropic  JournaledGrain +    │
│                   Ollama/GitHub     Qdrant (Memory)     │
└─────────────────────────────────────────────────────────┘
```

### Interaction behaviour (match hex1b.dev exactly)

- Hover any box → highlight it (glow), show description + optional link in the panel below
- Click to lock/unlock (locked = border stays highlighted, "click to unlock" hint in panel)
- Panel shows: title, 2–3 sentence accurate description sourced from the code you read,
  optional `Learn more →` link pointing to the relevant `/guide/*` page
- Default state (nothing hovered): show `👆 Hover any component to explore · click to lock`

### Technical requirements

- Pure `<script setup lang="ts">` — no Options API
- SVG for the diagram itself (not canvas, not HTML divs)
- All node data in a typed `Record<string, Node>` — no hardcoded strings scattered in template
- Use VitePress CSS variables throughout (`--vp-c-brand-1`, `--vp-c-bg-soft`, `--vp-c-text-1`,
  `--vp-c-divider`, etc.) so it works in both light and dark mode without any media queries
- `<style scoped>` only, no global styles
- All arrow markers defined in `<defs>` — two variants: active colour and dimmed
- `filter: drop-shadow(...)` for the hover glow — keep it subtle
- Clickable sub-boxes inside the cluster (one per agent group), each with their own node data

### Descriptions to write (derive from the code you read in Step 1)

Write accurate 2–3 sentence descriptions for every node. Do not paraphrase docs —
derive them from what you actually saw in the source. Examples of what to cover:

| Node | Key facts to surface |
|---|---|
| ThreadAgent | Two tools: `SendToAgent` (8 named agents) + `Orchestrate`; context providers; `ExecuteDelegation` → AgentSelector |
| AgentSelectorAgent | Queries `AgentRegistry.SearchAsync()`, LLM picks team, returns `SelectionResult` with `Ready/NeedsClarification/CannotHandle` |
| CodeOrchestratorAgent | `ScriptGenerator` → C# script referencing cluster via `AddIAWClient()` → `OrchestrationCompiler` (Roslyn) → `dotnet run` |
| TelegramUIAgent | Formatting-only grain, `[Llm<Fast>]`, `MaxHistoryMessages = 0`, no tools, formats raw text to `RichOutput` with inline buttons |
| Memory agents | 5 grains: UserMemory, ProjectMemory, PatternMemory, EpisodeMemory, CodeMemory; Qdrant embeddings; injected via `MemoryContextProvider` |
| LLM Wrappers | 14 agents, each wrapping one model via `[Llm<T>]`; used by CodeOrchestrator for model fan-out and comparison |
| Infrastructure | Shell, FileSystem, Git, Aspire, IAWSystem — agent names exactly as in `AgentInterfaceResolver` |
| CSharp agents | Roslyn (AST analysis), DotNet (SDK ops), GitHub (PRs/issues/CI), NuGet (package resolution) |
| Durable State | JournaledGrain, `DurableChatHistoryProvider` with `ChatReducer` + `HistorySummarizer`; L1/L2/L3 context tiers |
| Scheduling | `Orleans.DurableJobs` via `ILocalDurableJobManager`; one-shot and recurring; `IDurableJobHandler.OnJobDueAsync` |

---

## Step 3 — Wire into VitePress

### 3.1 Register the component

Edit `website/.vitepress/theme/index.ts` — add alongside the existing `BehaviorTabs` registration:

```ts
import ArchitectureDiagram from './components/ArchitectureDiagram.vue'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('BehaviorTabs', BehaviorTabs)
    app.component('ArchitectureDiagram', ArchitectureDiagram)
  }
}
```

### 3.2 Place in architecture.md

Keep all existing content. Insert `<ArchitectureDiagram />` directly after the intro
paragraph (before the `## Three-Tier Hierarchy` heading):

```md
# Architecture

This page covers ...

<ArchitectureDiagram />

## Three-Tier Hierarchy
...
```

---

## Step 4 — Verify

```bash
cd website
npm install        # if node_modules absent
npm run dev        # VitePress dev server
```

Open the architecture page. Confirm:
- [ ] Diagram renders without console errors
- [ ] Every box is hoverable (description panel updates)
- [ ] Click locks, second click on same box unlocks
- [ ] Works in both light and dark mode (toggle with VitePress theme switcher)
- [ ] No TypeScript errors (`npm run build` passes)

---

## Constraints

- No `any` types in TypeScript
- No inline `style=""` attributes in SVG — use CSS classes only
- No hardcoded hex colors — CSS variables only
- The component must be fully self-contained in one `.vue` file
- Do not modify any other files except `theme/index.ts` and `guide/architecture.md`
