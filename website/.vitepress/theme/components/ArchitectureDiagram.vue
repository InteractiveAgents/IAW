<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'

interface DiagramNode {
  label: string
  description: string
  link?: string
}

interface ArrowDef {
  x1: number; y1: number; x2: number; y2: number
  from: string; to: string
  label?: string; labelX?: number; labelY?: number
  dashed?: boolean
}

const nodes: Record<string, DiagramNode> = {
  telegram: {
    label: 'Telegram',
    description: 'Handles all Telegram interactions: text messages, voice transcription (OGG\u2192WAV\u2192Whisper), callback queries, and forum topics. Streams progress messages and coordinates with ThreadAgent for LLM responses. TelegramUIAgent formats output as RichOutput with inline buttons.',
    link: '/IAW/guide/telegram-bot',
  },
  mcp: {
    label: 'MCP Server',
    description: 'ModelContextProtocol bridge on port 5300 exposing agent operations: agent_list_all, assistant_chat, agent_send_message, agent_get_status, agent_assign_task, agent_get_events, and agent_get_metrics. Enables Claude Code and other MCP clients to interact with the agent cluster.',
    link: '/IAW/guide/mcp-server',
  },
  devui: {
    label: 'DevUI',
    description: 'Blazor web application for direct agent interaction. Connects to the Orleans cluster as an IAW client via AddIAWClient() and provides a development-focused interface for testing and debugging agent conversations.',
  },
  thread: {
    label: 'ThreadAgent',
    description: 'User-facing conversational thread (Orleans grain) with two tools: SendToAgent for single-agent delegation and Orchestrate for multi-agent workflows. Enriches prompts via UserContextProvider, RAGContextProvider (Qdrant), and MemoryContextProvider before each LLM call. Maintains up to 20 messages of history.',
    link: '/IAW/guide/architecture',
  },
  telegramui: {
    label: 'TelegramUI',
    description: 'Formatting-only grain using [Llm<Fast>] with zero history. Bypasses the Agent pipeline to avoid recursive tool calls, directly calling ChatClient to format raw text into RichOutput with inline buttons and suggestions for Telegram.',
    link: '/IAW/guide/telegram-features',
  },
  direct: {
    label: 'Direct Call',
    description: 'Simple single-agent execution path. ThreadAgent resolves the target agent interface via AgentInterfaceResolver, gets the grain instance scoped to the thread ID, and calls IAgent.GetResponse() directly. Response output is truncated at 4KB.',
    link: '/IAW/guide/communication',
  },
  selector: {
    label: 'AgentSelector',
    description: 'LLM-based agent router using [Llm<Balanced>]. Queries AgentRegistry.SearchAsync() to find candidate agents by relevance score, filters out LLM-namespace agents, then uses the LLM to select the best team. Returns SelectionResult with status Ready, NeedsClarification, or CannotHandle.',
    link: '/IAW/guide/orchestration',
  },
  orchestrator: {
    label: 'CodeOrchestrator',
    description: 'Generates standalone C# console apps connecting to the Orleans cluster via IAWCluster.Connect(). ScriptGenerator produces the code, OrchestrationCompiler validates with Roslyn, then dotnet run executes out-of-process. Uses [Llm<Reasoning>] with up to 3 compilation retries.',
    link: '/IAW/guide/orchestration',
  },
  infrastructure: {
    label: 'Infrastructure',
    description: 'Five infrastructure agents: Shell (command execution with 120s timeout), FileSystem (workspace I/O with boundary validation), Git (status/commit/diff/log), Aspire (deployment and health monitoring via MCP), and IAWSystem (coordinator delegating to specialists).',
    link: '/IAW/guide/tools',
  },
  csharp: {
    label: 'C#',
    description: 'Four .NET-specialized agents: Roslyn (full solution-aware code intelligence via Microsoft.CodeAnalysis), DotNet (build/test/format with event publishing), GitHub (release watching and issue creation), and NuGet (package update monitoring via nuget.org API).',
    link: '/IAW/guide/building-agents',
  },
  memory: {
    label: 'Memory',
    description: 'Five memory grains: UserMemory, ProjectMemory, PatternMemory, EpisodeMemory, and CodeMemory. Each stores MemoryEntry records with Qdrant vector embeddings for semantic search, plus daily maintenance jobs for decay (0.95\u00d7) and consolidation.',
    link: '/IAW/guide/memory',
  },
  llm: {
    label: 'LLM',
    description: '14 agents inheriting LlmAgentBase, each wrapping a specific model via [Llm<T>]: Claude (Haiku/Sonnet/Opus), GPT (4o/Mini/5.2/5.3/5.4 variants), Gemini, Grok, Llama, and Qwen. Used by CodeOrchestrator for model fan-out and comparison.',
    link: '/IAW/guide/llm-agents',
  },
  knowledge: {
    label: 'Knowledge',
    description: 'Records and retrieves project metadata: architectural decisions with rationale and outcome tracking, design patterns, coding conventions, and tech stack details. Stores all data as structured JSON state entries.',
  },
  userprofile: {
    label: 'UserProfile',
    description: 'Lightweight DurableGrain storing user-specific state: preferences as key-value pairs, project registrations mapping slugs to Telegram topic IDs, and semantic facts. No LLM integration \u2014 pure state management for personalization.',
  },
  aspire: {
    label: 'Aspire + OTel',
    description: 'Aspire AppHost orchestrates the distributed topology via AddIAW(): Orleans cluster, Azurite blob storage, Qdrant vector DB, LLM provider configuration, and all services. OpenTelemetry exports traces and metrics to the Aspire dashboard.',
    link: '/IAW/guide/getting-started',
  },
  providers: {
    label: 'LLM Providers',
    description: 'Model providers configured via WithLLM<T>().AsFast/AsBalanced/AsReasoning() in the AppHost. Supports OpenAI, Anthropic, Ollama, and GitHub Models. The first WithLLM<T>() call becomes the default non-keyed IChatClient.',
    link: '/IAW/guide/llm-agents',
  },
  state: {
    label: 'Durable State',
    description: 'JournaledGrain persistence via Orleans Journaling with four durable collections: state, eventLog, history, and scheduledJobs. DurableChatHistoryProvider manages history through ChatReducer (400KB limit) and HistorySummarizer (40+ messages). Qdrant provides L3 vector store.',
    link: '/IAW/guide/persistence',
  },
}

const arrows: ArrowDef[] = [
  { x1: 140, y1: 64, x2: 300, y2: 112, from: 'telegram', to: 'thread' },
  { x1: 400, y1: 64, x2: 365, y2: 112, from: 'mcp', to: 'thread' },
  { x1: 660, y1: 64, x2: 430, y2: 112, from: 'devui', to: 'thread' },
  { x1: 275, y1: 164, x2: 142, y2: 216, from: 'thread', to: 'direct',
    label: 'SendToAgent', labelX: 175, labelY: 184 },
  { x1: 430, y1: 164, x2: 390, y2: 216, from: 'thread', to: 'selector',
    label: 'Orchestrate', labelX: 455, labelY: 184 },
  { x1: 480, y1: 244, x2: 530, y2: 244, from: 'selector', to: 'orchestrator' },
  { x1: 560, y1: 138, x2: 610, y2: 138, from: 'thread', to: 'telegramui', dashed: true },
  { x1: 142, y1: 272, x2: 142, y2: 325, from: 'direct', to: 'cluster' },
  { x1: 630, y1: 272, x2: 630, y2: 325, from: 'orchestrator', to: 'cluster' },
  { x1: 150, y1: 465, x2: 150, y2: 520, from: 'cluster', to: 'aspire' },
  { x1: 400, y1: 465, x2: 400, y2: 520, from: 'cluster', to: 'providers' },
  { x1: 650, y1: 465, x2: 650, y2: 520, from: 'cluster', to: 'state' },
]

const particles = computed(() =>
  arrows.map((a, i) => {
    const dx = a.x2 - a.x1
    const dy = a.y2 - a.y1
    const len = Math.sqrt(dx * dx + dy * dy)
    const dur = Math.max(1, len / 70)
    return {
      path: `M${a.x1},${a.y1} L${a.x2},${a.y2}`,
      dur: `${dur.toFixed(1)}s`,
      delay: `${(i * 0.37).toFixed(2)}s`,
    }
  }),
)

const wrapperRef = ref<HTMLElement | null>(null)
const visible = ref(false)
const hoveredNode = ref<string | null>(null)
const lockedNode = ref<string | null>(null)
const activeNode = computed(() => lockedNode.value ?? hoveredNode.value)

onMounted(() => {
  const observer = new IntersectionObserver(
    ([entry]) => {
      if (entry.isIntersecting) {
        visible.value = true
        observer.disconnect()
      }
    },
    { threshold: 0.1 },
  )
  if (wrapperRef.value) observer.observe(wrapperRef.value)
})

function onEnter(id: string) {
  if (!lockedNode.value) hoveredNode.value = id
}
function onLeave() {
  if (!lockedNode.value) hoveredNode.value = null
}
function onClick(id: string) {
  if (lockedNode.value === id) {
    lockedNode.value = null
    hoveredNode.value = null
  } else {
    lockedNode.value = id
    hoveredNode.value = id
  }
}

function nc(id: string): string {
  const a = activeNode.value
  if (!a) return 'node'
  return a === id ? 'node node-active' : 'node node-dimmed'
}

function ac(from: string, to: string): string {
  const a = activeNode.value
  if (!a) return ''
  if (a === from) return 'arrow-active'
  if (a === to) return 'arrow-incoming'
  return 'arrow-dimmed'
}

function am(from: string): string {
  return activeNode.value === from ? 'url(#ah-brand)' : 'url(#ah)'
}
</script>

<template>
  <div ref="wrapperRef" class="arch-wrapper" :class="{ visible }">
    <div class="arch-canvas">
      <svg class="arch-svg" viewBox="0 0 800 580" xmlns="http://www.w3.org/2000/svg">
        <defs>
          <marker id="ah" viewBox="0 0 10 7" refX="10" refY="3.5"
            markerWidth="7" markerHeight="5" orient="auto">
            <path d="M0,0 L10,3.5 L0,7Z" class="mk" />
          </marker>
          <marker id="ah-brand" viewBox="0 0 10 7" refX="10" refY="3.5"
            markerWidth="7" markerHeight="5" orient="auto">
            <path d="M0,0 L10,3.5 L0,7Z" class="mk-brand" />
          </marker>
        </defs>

        <!-- Ambient flow particles -->
        <g class="row row-particles">
          <circle v-for="(p, i) in particles" :key="i" r="2.5" class="particle">
            <animateMotion :dur="p.dur" :begin="p.delay"
              repeatCount="indefinite" :path="p.path" />
          </circle>
        </g>

        <!-- Arrows -->
        <g class="row row-arrows">
          <g v-for="(a, i) in arrows" :key="i" :class="['ag', ac(a.from, a.to)]">
            <line :x1="a.x1" :y1="a.y1" :x2="a.x2" :y2="a.y2"
              class="al" :marker-end="am(a.from)"
              :stroke-dasharray="a.dashed ? '4 3' : undefined" />
            <text v-if="a.label" :x="a.labelX" :y="a.labelY" class="albl">
              {{ a.label }}
            </text>
          </g>
        </g>

        <!-- Thread ambient glow -->
        <rect class="thread-glow" x="166" y="108" width="398" height="60" rx="14" />

        <!-- ===== ROW 1 ===== -->
        <g class="row row-1">
          <g :class="nc('telegram')" @mouseenter="onEnter('telegram')" @mouseleave="onLeave" @click="onClick('telegram')">
            <rect x="50" y="20" width="180" height="44" rx="10" />
            <text x="140" y="47" class="nl">Telegram</text>
          </g>
          <g :class="nc('mcp')" @mouseenter="onEnter('mcp')" @mouseleave="onLeave" @click="onClick('mcp')">
            <rect x="310" y="20" width="180" height="44" rx="10" />
            <text x="400" y="47" class="nl">MCP Server</text>
          </g>
          <g :class="nc('devui')" @mouseenter="onEnter('devui')" @mouseleave="onLeave" @click="onClick('devui')">
            <rect x="570" y="20" width="180" height="44" rx="10" />
            <text x="660" y="47" class="nl">DevUI</text>
          </g>
        </g>

        <!-- ===== ROW 2 ===== -->
        <g class="row row-2">
          <g :class="nc('thread')" @mouseenter="onEnter('thread')" @mouseleave="onLeave" @click="onClick('thread')">
            <rect x="170" y="112" width="390" height="52" rx="10" />
            <text x="365" y="143" class="nl">ThreadAgent</text>
          </g>
          <g :class="nc('telegramui')" @mouseenter="onEnter('telegramui')" @mouseleave="onLeave" @click="onClick('telegramui')">
            <rect x="610" y="112" width="130" height="52" rx="10" />
            <text x="675" y="143" class="nl ns">TelegramUI</text>
          </g>
        </g>

        <!-- ===== ROW 3 ===== -->
        <g class="row row-3">
          <g :class="nc('direct')" @mouseenter="onEnter('direct')" @mouseleave="onLeave" @click="onClick('direct')">
            <rect x="35" y="216" width="215" height="56" rx="10" />
            <text x="142" y="249" class="nl">Direct Call</text>
          </g>
          <g :class="nc('selector')" @mouseenter="onEnter('selector')" @mouseleave="onLeave" @click="onClick('selector')">
            <rect x="300" y="216" width="180" height="56" rx="10" />
            <text x="390" y="249" class="nl">AgentSelector</text>
          </g>
          <g :class="nc('orchestrator')" @mouseenter="onEnter('orchestrator')" @mouseleave="onLeave" @click="onClick('orchestrator')">
            <rect x="530" y="216" width="200" height="56" rx="10" />
            <text x="630" y="249" class="nl ns">CodeOrchestrator</text>
          </g>
        </g>

        <!-- ===== ROW 4: Cluster ===== -->
        <g class="row row-4">
          <rect class="cluster-bg" x="25" y="325" width="750" height="140" rx="12" />
          <rect class="cluster-border" x="25" y="325" width="750" height="140" rx="12" />
          <rect class="cluster-lbl-bg" x="300" y="316" width="200" height="18" rx="4" />
          <text x="400" y="329" class="cluster-lbl">Agent Cluster (Orleans Silo)</text>

          <g :class="nc('infrastructure')" @mouseenter="onEnter('infrastructure')" @mouseleave="onLeave" @click="onClick('infrastructure')">
            <rect x="40" y="350" width="135" height="95" rx="8" />
            <text x="107" y="403" class="nl ns">Infrastructure</text>
          </g>
          <g :class="nc('csharp')" @mouseenter="onEnter('csharp')" @mouseleave="onLeave" @click="onClick('csharp')">
            <rect x="190" y="350" width="125" height="95" rx="8" />
            <text x="252" y="403" class="nl ns">C#</text>
          </g>
          <g :class="nc('memory')" @mouseenter="onEnter('memory')" @mouseleave="onLeave" @click="onClick('memory')">
            <rect x="330" y="350" width="125" height="95" rx="8" />
            <text x="392" y="403" class="nl ns">Memory</text>
          </g>
          <g :class="nc('llm')" @mouseenter="onEnter('llm')" @mouseleave="onLeave" @click="onClick('llm')">
            <rect x="470" y="350" width="135" height="95" rx="8" />
            <text x="537" y="403" class="nl ns">LLM</text>
          </g>
          <g :class="nc('knowledge')" @mouseenter="onEnter('knowledge')" @mouseleave="onLeave" @click="onClick('knowledge')">
            <rect x="620" y="350" width="140" height="42" rx="8" />
            <text x="690" y="376" class="nl ns">Knowledge</text>
          </g>
          <g :class="nc('userprofile')" @mouseenter="onEnter('userprofile')" @mouseleave="onLeave" @click="onClick('userprofile')">
            <rect x="620" y="403" width="140" height="42" rx="8" />
            <text x="690" y="429" class="nl ns">UserProfile</text>
          </g>
        </g>

        <!-- ===== ROW 5 ===== -->
        <g class="row row-5">
          <g :class="nc('aspire')" @mouseenter="onEnter('aspire')" @mouseleave="onLeave" @click="onClick('aspire')">
            <rect x="50" y="520" width="200" height="44" rx="10" />
            <text x="150" y="547" class="nl ns">Aspire + OTel</text>
          </g>
          <g :class="nc('providers')" @mouseenter="onEnter('providers')" @mouseleave="onLeave" @click="onClick('providers')">
            <rect x="300" y="520" width="200" height="44" rx="10" />
            <text x="400" y="547" class="nl ns">LLM Providers</text>
          </g>
          <g :class="nc('state')" @mouseenter="onEnter('state')" @mouseleave="onLeave" @click="onClick('state')">
            <rect x="550" y="520" width="200" height="44" rx="10" />
            <text x="650" y="547" class="nl ns">Durable State</text>
          </g>
        </g>
      </svg>
    </div>

    <!-- Description Panel -->
    <div class="dp" :class="{ 'dp-active': activeNode }">
      <Transition name="fade" mode="out-in">
        <div v-if="activeNode" :key="activeNode" class="dp-body">
          <div class="dp-head">
            <h3>{{ nodes[activeNode].label }}</h3>
            <span v-if="lockedNode" class="dp-hint">click to unlock</span>
          </div>
          <p>{{ nodes[activeNode].description }}</p>
          <a v-if="nodes[activeNode].link" :href="nodes[activeNode].link" class="dp-link">
            Learn more &rarr;
          </a>
        </div>
        <p v-else key="default" class="dp-empty">
          &#x1F446; Hover any component to explore &#xb7; click to lock
        </p>
      </Transition>
    </div>
  </div>
</template>

<style scoped>
/* ── Layout ── */
.arch-wrapper {
  max-width: 860px;
  margin: 32px auto;
  overflow-x: auto;
}

.arch-canvas {
  background: var(--vp-c-bg-alt);
  border: 1px solid var(--vp-c-divider);
  border-radius: 14px;
  padding: 6px 0;
}

.arch-svg {
  width: 100%;
  min-width: 560px;
  height: auto;
  display: block;
}

/* ── Entrance ── */
.row {
  opacity: 0;
  transform: translateY(16px);
}

.visible .row {
  animation: enter 0.5s ease-out forwards;
}

.visible .row-1 { animation-delay: 0.05s; }
.visible .row-2 { animation-delay: 0.12s; }
.visible .row-3 { animation-delay: 0.22s; }
.visible .row-4 { animation-delay: 0.32s; }
.visible .row-5 { animation-delay: 0.42s; }
.visible .row-arrows { animation-delay: 0.5s; }
.visible .row-particles { animation-delay: 0.7s; }

@keyframes enter {
  from { opacity: 0; transform: translateY(16px); }
  to   { opacity: 1; transform: translateY(0); }
}

/* ── Particles ── */
.particle {
  fill: var(--vp-c-brand-1);
  opacity: 0.3;
}

/* ── Thread ambient glow ── */
.thread-glow {
  fill: none;
  stroke: var(--vp-c-brand-1);
  stroke-width: 1.5;
  opacity: 0;
  animation: breathe 3.5s ease-in-out infinite;
  pointer-events: none;
}

@keyframes breathe {
  0%, 100% { opacity: 0; }
  50%      { opacity: 0.18; }
}

/* ── Arrows ── */
.al {
  stroke: var(--vp-c-divider);
  stroke-width: 1.2;
  fill: none;
  transition: stroke 0.25s, stroke-width 0.25s;
}

.albl {
  fill: var(--vp-c-text-3);
  font-family: var(--vp-font-family-base);
  font-size: 10px;
  text-anchor: middle;
  pointer-events: none;
  transition: fill 0.25s;
}

.ag { transition: opacity 0.3s; }

.arrow-active .al {
  stroke: var(--vp-c-brand-1);
  stroke-width: 1.8;
  stroke-dasharray: 6 3;
  animation: flow 0.45s linear infinite;
}
.arrow-active .albl { fill: var(--vp-c-brand-1); }
.arrow-incoming { opacity: 0.4; }
.arrow-dimmed   { opacity: 0.15; }

@keyframes flow { to { stroke-dashoffset: -9; } }

.mk      { fill: var(--vp-c-divider); }
.mk-brand { fill: var(--vp-c-brand-1); }

/* ── Cluster ── */
.cluster-bg {
  fill: var(--vp-c-bg-soft);
  opacity: 0.5;
}

.cluster-border {
  fill: none;
  stroke: var(--vp-c-divider);
  stroke-width: 1;
  stroke-dasharray: 5 4;
  animation: drift 14s linear infinite;
}

@keyframes drift { to { stroke-dashoffset: -36; } }

.cluster-lbl-bg {
  fill: var(--vp-c-bg-alt);
  stroke: none;
}

.cluster-lbl {
  fill: var(--vp-c-text-3);
  font-family: var(--vp-font-family-base);
  font-size: 10.5px;
  font-weight: 500;
  text-anchor: middle;
  letter-spacing: 0.4px;
  pointer-events: none;
}

/* ── Nodes ── */
.node {
  cursor: pointer;
  transition: opacity 0.3s;
}

.node rect {
  fill: var(--vp-c-bg-soft);
  stroke: var(--vp-c-divider);
  stroke-width: 1;
  transition: stroke 0.2s, filter 0.3s, stroke-width 0.2s;
}

.node:hover rect {
  stroke: var(--vp-c-text-3);
}

.node-active rect {
  stroke: var(--vp-c-brand-1);
  stroke-width: 1.5;
  animation: glow 2.4s ease-in-out infinite;
}

.node-dimmed { opacity: 0.2; }

@keyframes glow {
  0%, 100% { filter: drop-shadow(0 0 3px var(--vp-c-brand-soft)); }
  50%      { filter: drop-shadow(0 0 9px var(--vp-c-brand-soft)); }
}

/* ── Node labels ── */
.nl {
  fill: var(--vp-c-text-1);
  font-family: var(--vp-font-family-base);
  font-size: 13px;
  font-weight: 600;
  text-anchor: middle;
  pointer-events: none;
}

.ns { font-size: 12px; }

/* ── Description panel ── */
.dp {
  margin-top: 14px;
  padding: 14px 18px;
  border-radius: 10px;
  border: 1px solid var(--vp-c-divider);
  background: var(--vp-c-bg-soft);
  min-height: 64px;
  transition: border-color 0.3s, box-shadow 0.3s;
  opacity: 0;
  transform: translateY(8px);
}

.visible .dp {
  animation: enter 0.45s ease-out 0.65s forwards;
}

.dp-active {
  border-color: var(--vp-c-brand-1);
  box-shadow: 0 0 20px -6px var(--vp-c-brand-soft);
}

.dp-head {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}

.dp-head h3 {
  margin: 0 0 4px;
  font-size: 15px;
  font-weight: 600;
  color: var(--vp-c-text-1);
}

.dp p {
  margin: 0;
  font-size: 14px;
  line-height: 1.55;
  color: var(--vp-c-text-2);
}

.dp-link {
  display: inline-block;
  margin-top: 6px;
  font-size: 13px;
  color: var(--vp-c-brand-1);
  text-decoration: none;
  font-weight: 500;
}

.dp-link:hover { text-decoration: underline; }

.dp-hint {
  font-size: 11px;
  color: var(--vp-c-text-3);
  white-space: nowrap;
}

.dp-empty {
  text-align: center;
  color: var(--vp-c-text-3) !important;
  padding: 6px 0;
}

/* ── Panel transition ── */
.fade-enter-active { transition: opacity 0.18s, transform 0.18s; }
.fade-leave-active { transition: opacity 0.1s; }
.fade-enter-from   { opacity: 0; transform: translateY(4px); }
.fade-leave-to     { opacity: 0; }
</style>
