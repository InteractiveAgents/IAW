# Architecture Diagram Redesign — Aspire Topology

**Date:** 2026-03-31

## Goal

Replace the current flat-rows architecture diagram with an Aspire Topology layout that shows the real system structure: Aspire AppHost wrapping Clients, Orleans Cluster, and Resources. Place it on the homepage (replacing BehaviorTabs) and keep it on the architecture guide page.

## Layout

```
┌─────────────────────────────────────────────────────────────────┐
│  ASPIRE APPHOST                                                  │
│                                                                   │
│  ┌─────────┐       ┌─────────────────────────────┐   ┌─────────┐│
│  │ CLIENTS  │       │     ORLEANS CLUSTER          │   │RESOURCES││
│  │          │  →→→  │                              │   │         ││
│  │ Telegram │       │  ┌────────┐  ┌────────────┐  │   │ Azurite ││
│  │ MCP      │       │  │Orchest.│  │Infrastructure│ │→→→│ Qdrant  ││
│  │ DevUI    │       │  ├────────┤  ├────────────┤  │   │ LLM APIs││
│  │          │       │  │  C#    │  │   Memory   │  │   │         ││
│  │          │       │  ├────────┤  ├────────────┤  │   │         ││
│  │          │       │  │  LLM   │  │            │  │   │         ││
│  └─────────┘       └─────────────────────────────┘   └─────────┘│
└─────────────────────────────────────────────────────────────────┘
                              ↕
                    ┌──────────────────┐
                    │ Description Panel │
                    └──────────────────┘
```

### Three columns inside the Aspire AppHost border

1. **Clients** (left): Telegram, MCP, DevUI — each a hoverable node
2. **Orleans Cluster** (center, dashed border): 5 agent group blocks as peers
   - Orchestration (Thread, Selector, CodeOrchestrator)
   - Infrastructure (Shell, FS, Git, Aspire, System)
   - C# (Roslyn, DotNet, GitHub, NuGet)
   - Memory (5 agents, Qdrant search)
   - LLM (14 models, multi-provider)
3. **Resources** (right): Azurite, Qdrant, LLM APIs — each a hoverable node

### Labels

- Outer border: `ASPIRE APPHOST` (top-left, breaking the border line)
- Inner dashed border: `ORLEANS CLUSTER` (top-left, breaking the border line)
- Column headers: `CLIENTS`, `RESOURCES` (small uppercase above each column)

## Nodes (13 total)

Each node has: short label (shown in diagram), description (shown in panel on hover), optional link.

| ID | Label | Description summary |
|---|---|---|
| telegram | Telegram | Telegram bot with voice, forums, streaming |
| mcp | MCP | MCP server :5300, 7 agent tools |
| devui | DevUI | Blazor web UI via AddIAWClient() |
| orchestration | Orchestration | Thread → Selector → CodeOrchestrator routing |
| infrastructure | Infrastructure | Shell, FS, Git, Aspire, IAWSystem agents |
| csharp | C# | Roslyn, DotNet, GitHub, NuGet agents |
| memory | Memory | 5 memory grains with Qdrant embeddings |
| llm | LLM | 14 model agents via [Llm<T>] |
| azurite | Azurite | Blob storage via Aspire Azure emulator |
| qdrant | Qdrant | Vector DB for embeddings and RAG |
| llmapis | LLM APIs | OpenAI, Anthropic, Ollama, GitHub Models |

Note: "ASPIRE APPHOST" and "ORLEANS CLUSTER" are labels, not interactive nodes.

## Animations

1. **Flowing particles**: Small dots (r=2.5, brand color, 30% opacity) travel along connection lines continuously. One particle per connection, staggered start times.
2. **Animated cluster border**: Dashed border slowly drifts (stroke-dashoffset animation, ~14s cycle).
3. **Staggered entrance**: Each column/section fades in with slide-up, triggered by IntersectionObserver. Clients → Cluster → Resources → Arrows → Particles.
4. **Hover glow**: Active node gets pulsing drop-shadow (brand-soft, 2.4s cycle).
5. **Active arrows**: Outgoing connection lines from hovered node get flowing dashed animation. Incoming arrows dim to 40%. Unrelated arrows dim to 15%.

## Interaction

- Hover any node → highlight with glow, show description in panel below
- Click to lock/unlock (locked = stays highlighted, panel shows "click to unlock")
- Default panel: "👆 Hover any component to explore · click to lock"
- Panel transitions: Vue `<Transition>` with fade + slide

## Connection Lines (SVG arrows)

- Clients → Cluster: 3 horizontal arrows (Telegram, MCP, DevUI → cluster left edge)
- Cluster → Resources: 3 horizontal arrows (cluster right edge → Azurite, Qdrant, LLM APIs)
- Each arrow has marker-end arrowhead

## Placement

### Homepage (`website/index.md`)
- Replace `<BehaviorTabs />` with `<ArchitectureDiagram />`
- Keep Quick Start and Key Features sections below

### Architecture page (`website/guide/architecture.md`)
- Keep `<ArchitectureDiagram />` in existing position (after intro, before Three-Tier Hierarchy)

## Technical

- Single `.vue` file: `website/.vitepress/theme/components/ArchitectureDiagram.vue`
- Pure SVG diagram (not HTML divs)
- `<script setup lang="ts">` composition API
- All node data in typed `Record<string, DiagramNode>`
- VitePress CSS variables only (no hardcoded colors)
- `<style scoped>`, no global styles
- Works in both light and dark mode
- SVG viewBox for responsive scaling, min-width for mobile scroll

## Files Changed

1. `website/.vitepress/theme/components/ArchitectureDiagram.vue` — full rewrite
2. `website/index.md` — replace `<BehaviorTabs />` with `<ArchitectureDiagram />`
3. `website/guide/architecture.md` — no change (already has the component)
4. `website/.vitepress/theme/index.ts` — no change (already registered)

## Not Changed

- `BehaviorTabs.vue` stays in the codebase (can be used on guide pages later)
- No new dependencies
