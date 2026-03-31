<script setup lang="ts">
import { ref, computed } from 'vue'

interface DiagramNode {
  label: string
  description: string
  link?: string
}

interface ArrowDef {
  x1: number
  y1: number
  x2: number
  y2: number
  nodes: string[]
  label?: string
  labelX?: number
  labelY?: number
  dashed?: boolean
}

const nodes: Record<string, DiagramNode> = {
  telegram: {
    label: 'Telegram Bot',
    description:
      'Handles all Telegram interactions: text messages, voice transcription (OGG\u2192WAV\u2192Whisper), callback queries, and forum topics. Streams progress messages and coordinates with ThreadAgent for LLM responses. TelegramUIAgent formats output as RichOutput with inline buttons.',
    link: '/IAW/guide/telegram-bot',
  },
  mcp: {
    label: 'MCP Server',
    description:
      'ModelContextProtocol bridge on port 5300 exposing agent operations: agent_list_all, assistant_chat, agent_send_message, agent_get_status, agent_assign_task, agent_get_events, and agent_get_metrics. Enables Claude Code and other MCP clients to interact with the agent cluster.',
    link: '/IAW/guide/mcp-server',
  },
  devui: {
    label: 'DevUI',
    description:
      'Blazor web application for direct agent interaction. Connects to the Orleans cluster as an IAW client via AddIAWClient() and provides a development-focused interface for testing and debugging agent conversations.',
  },
  thread: {
    label: 'ThreadAgent',
    description:
      'User-facing conversational thread (Orleans grain) with two tools: SendToAgent for single-agent delegation and Orchestrate for multi-agent workflows. Enriches prompts via UserContextProvider, RAGContextProvider (Qdrant), and MemoryContextProvider before each LLM call. Maintains up to 20 messages of history.',
    link: '/IAW/guide/architecture',
  },
  telegramui: {
    label: 'TelegramUIAgent',
    description:
      'Formatting-only grain using [Llm<Fast>] with zero history (MaxHistoryMessages = 0). Bypasses the Agent pipeline to avoid recursive tool calls, directly calling ChatClient to format raw text into RichOutput with inline buttons and suggestions for Telegram.',
    link: '/IAW/guide/telegram-features',
  },
  direct: {
    label: 'Direct Call',
    description:
      'Simple single-agent execution path. ThreadAgent resolves the target agent interface via AgentInterfaceResolver, gets the grain instance scoped to the thread ID, and calls IAgent.GetResponse() directly. Response output is truncated at 4KB before returning to the thread.',
    link: '/IAW/guide/communication',
  },
  selector: {
    label: 'AgentSelector',
    description:
      'LLM-based agent router using [Llm<Balanced>]. Queries AgentRegistry.SearchAsync() to find candidate agents by relevance score, filters out LLM-namespace agents, then uses the LLM to select the best team. Returns SelectionResult with status Ready, NeedsClarification, or CannotHandle.',
    link: '/IAW/guide/orchestration',
  },
  orchestrator: {
    label: 'CodeOrchestrator',
    description:
      'Generates standalone C# console apps connecting to the Orleans cluster via IAWCluster.Connect(). ScriptGenerator produces the code, OrchestrationCompiler validates with Roslyn, then dotnet run executes out-of-process. Uses [Llm<Reasoning>] with up to 3 compilation retry cycles.',
    link: '/IAW/guide/orchestration',
  },
  infrastructure: {
    label: 'Infrastructure',
    description:
      'Five infrastructure agents: Shell (command execution with 120s timeout), FileSystem (workspace I/O with boundary validation), Git (status/commit/diff/log), Aspire (deployment and health monitoring via MCP), and IAWSystem (coordinator delegating to specialists). Most use [Llm<Fast>].',
    link: '/IAW/guide/tools',
  },
  csharp: {
    label: 'C# Agents',
    description:
      'Four .NET-specialized agents: Roslyn (full solution-aware code intelligence via Microsoft.CodeAnalysis \u2014 call graphs, type maps, pattern detection), DotNet (build/test/format with event publishing), GitHub (release watching and issue creation), and NuGet (package update monitoring via nuget.org API).',
    link: '/IAW/guide/building-agents',
  },
  memory: {
    label: 'Memory',
    description:
      'Five memory grains inheriting MemoryAgentBase: UserMemory, ProjectMemory, PatternMemory, EpisodeMemory, and CodeMemory. Each stores MemoryEntry records with Qdrant vector embeddings for semantic search, plus daily maintenance jobs for decay (0.95\u00d7) and consolidation.',
    link: '/IAW/guide/memory',
  },
  llm: {
    label: 'LLM Wrappers',
    description:
      '14 agents inheriting LlmAgentBase, each wrapping a specific model via [Llm<T>]: Claude (Haiku/Sonnet/Opus), GPT (4o/Mini/5.2/5.3/5.4 variants), Gemini, Grok, Llama, and Qwen. Used by CodeOrchestrator for model fan-out, comparison, and specialized reasoning tasks.',
    link: '/IAW/guide/llm-agents',
  },
  knowledge: {
    label: 'Knowledge',
    description:
      'Records and retrieves project metadata: architectural decisions with rationale and outcome tracking, design patterns, coding conventions, and tech stack details. Stores all data as structured JSON state entries accessible via tools like RecordDecision and ListPatterns.',
  },
  userprofile: {
    label: 'UserProfile',
    description:
      'Lightweight DurableGrain (not an Agent subclass) storing user-specific state: preferences as key-value pairs, project registrations mapping slugs to Telegram topic IDs, and semantic facts. No LLM integration, no tools \u2014 pure state management for personalization.',
  },
  aspire: {
    label: 'Aspire + OTel',
    description:
      'Aspire AppHost orchestrates the distributed topology via AddIAW(): Orleans cluster, Azurite blob storage, Qdrant vector DB, LLM provider configuration, and all services. OpenTelemetry exports traces (ActivitySource "IAW") and metrics to the Aspire dashboard.',
    link: '/IAW/guide/getting-started',
  },
  providers: {
    label: 'LLM Providers',
    description:
      'Model providers configured via WithLLM<T>().AsFast/AsBalanced/AsReasoning() in the AppHost. Supports OpenAI (GPT family), Anthropic (Claude family), Ollama (local Llama/Qwen), and GitHub Models. The first WithLLM<T>() call becomes the default non-keyed IChatClient.',
    link: '/IAW/guide/llm-agents',
  },
  state: {
    label: 'Durable State',
    description:
      'JournaledGrain persistence via Orleans Journaling with four durable collections: state, eventLog, history, and scheduledJobs. DurableChatHistoryProvider manages conversation history through ChatReducer (400KB limit) and HistorySummarizer (40+ message threshold). Qdrant provides L3 vector store for long-term memory recall.',
    link: '/IAW/guide/persistence',
  },
}

const arrows: ArrowDef[] = [
  { x1: 140, y1: 58, x2: 330, y2: 98, nodes: ['telegram', 'thread'] },
  { x1: 410, y1: 58, x2: 400, y2: 98, nodes: ['mcp', 'thread'] },
  { x1: 680, y1: 58, x2: 470, y2: 98, nodes: ['devui', 'thread'] },
  {
    x1: 290, y1: 158, x2: 162, y2: 210, nodes: ['thread', 'direct'],
    label: 'SendToAgent (1 agent)', labelX: 190, labelY: 177,
  },
  {
    x1: 460, y1: 158, x2: 445, y2: 210, nodes: ['thread', 'selector'],
    label: 'Orchestrate (N agents)', labelX: 520, labelY: 177,
  },
  { x1: 540, y1: 245, x2: 575, y2: 245, nodes: ['selector', 'orchestrator'] },
  { x1: 620, y1: 128, x2: 650, y2: 128, nodes: ['thread', 'telegramui'], dashed: true },
  { x1: 162, y1: 280, x2: 162, y2: 320, nodes: ['direct'] },
  { x1: 680, y1: 280, x2: 680, y2: 320, nodes: ['orchestrator'] },
  { x1: 150, y1: 480, x2: 150, y2: 520, nodes: ['aspire'] },
  { x1: 410, y1: 480, x2: 410, y2: 520, nodes: ['providers'] },
  { x1: 670, y1: 480, x2: 670, y2: 520, nodes: ['state'] },
]

const hoveredNode = ref<string | null>(null)
const lockedNode = ref<string | null>(null)
const activeNode = computed(() => lockedNode.value ?? hoveredNode.value)

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

function nodeClass(id: string): string {
  const a = activeNode.value
  if (!a) return 'node'
  return a === id ? 'node node-active' : 'node node-dimmed'
}

function arrowMod(connectedNodes: string[]): string {
  const a = activeNode.value
  if (!a) return ''
  return connectedNodes.includes(a) ? 'arrow-active' : 'arrow-dimmed'
}

function arrowMarker(connectedNodes: string[]): string {
  const a = activeNode.value
  return a && connectedNodes.includes(a) ? 'url(#ah-brand)' : 'url(#ah)'
}
</script>

<template>
  <div class="arch-wrapper">
    <svg class="arch-svg" viewBox="0 0 820 590" xmlns="http://www.w3.org/2000/svg">
      <defs>
        <marker id="ah" viewBox="0 0 10 7" refX="10" refY="3.5"
          markerWidth="8" markerHeight="6" orient="auto">
          <path d="M0,0 L10,3.5 L0,7Z" class="marker-fill" />
        </marker>
        <marker id="ah-brand" viewBox="0 0 10 7" refX="10" refY="3.5"
          markerWidth="8" markerHeight="6" orient="auto">
          <path d="M0,0 L10,3.5 L0,7Z" class="marker-brand" />
        </marker>
      </defs>

      <!-- Arrows (painted first, behind nodes) -->
      <g v-for="(a, i) in arrows" :key="i" :class="['arrow-group', arrowMod(a.nodes)]">
        <line :x1="a.x1" :y1="a.y1" :x2="a.x2" :y2="a.y2"
          class="arrow-line" :marker-end="arrowMarker(a.nodes)"
          :stroke-dasharray="a.dashed ? '5 3' : undefined" />
        <text v-if="a.label" :x="a.labelX" :y="a.labelY"
          class="arrow-label">{{ a.label }}</text>
      </g>

      <!-- ROW 4 cluster border (behind inner boxes) -->
      <rect class="cluster-bg" x="15" y="320" width="790" height="160" rx="10" />
      <rect class="cluster-border" x="15" y="320" width="790" height="160" rx="10" />
      <rect class="cluster-label-bg" x="310" y="311" width="200" height="18" rx="3" />
      <text x="410" y="324" class="cluster-title">Agent Cluster (Orleans Silo)</text>

      <!-- ==================== ROW 1: Entry Points ==================== -->
      <g :class="nodeClass('telegram')"
        @mouseenter="onEnter('telegram')" @mouseleave="onLeave"
        @click="onClick('telegram')">
        <rect x="40" y="10" width="200" height="48" rx="8" />
        <text x="140" y="38" class="node-title">Telegram Bot</text>
      </g>

      <g :class="nodeClass('mcp')"
        @mouseenter="onEnter('mcp')" @mouseleave="onLeave"
        @click="onClick('mcp')">
        <rect x="310" y="10" width="200" height="48" rx="8" />
        <text x="410" y="30" class="node-title">MCP Server</text>
        <text x="410" y="48" class="node-sub">:5300</text>
      </g>

      <g :class="nodeClass('devui')"
        @mouseenter="onEnter('devui')" @mouseleave="onLeave"
        @click="onClick('devui')">
        <rect x="580" y="10" width="200" height="48" rx="8" />
        <text x="680" y="30" class="node-title">DevUI</text>
        <text x="680" y="48" class="node-sub">Blazor</text>
      </g>

      <!-- ==================== ROW 2: Orchestration ==================== -->
      <g :class="nodeClass('thread')"
        @mouseenter="onEnter('thread')" @mouseleave="onLeave"
        @click="onClick('thread')">
        <rect x="180" y="98" width="440" height="60" rx="8" />
        <text x="400" y="122" class="node-title">ThreadAgent (Orleans Grain)</text>
        <text x="400" y="142" class="node-sub">context: User \u00b7 RAG \u00b7 Memory</text>
      </g>

      <g :class="nodeClass('telegramui')"
        @mouseenter="onEnter('telegramui')" @mouseleave="onLeave"
        @click="onClick('telegramui')">
        <rect x="650" y="98" width="140" height="60" rx="8" />
        <text x="720" y="122" class="node-title-sm">TelegramUI</text>
        <text x="720" y="142" class="node-sub">Formatter</text>
      </g>

      <!-- ==================== ROW 3: Routing ==================== -->
      <g :class="nodeClass('direct')"
        @mouseenter="onEnter('direct')" @mouseleave="onLeave"
        @click="onClick('direct')">
        <rect x="25" y="210" width="275" height="70" rx="8" />
        <text x="162" y="238" class="node-title">Direct Call</text>
        <text x="162" y="258" class="node-sub-mono">IAgent.GetResponse()</text>
      </g>

      <g :class="nodeClass('selector')"
        @mouseenter="onEnter('selector')" @mouseleave="onLeave"
        @click="onClick('selector')">
        <rect x="345" y="210" width="195" height="70" rx="8" />
        <text x="442" y="238" class="node-title">AgentSelector</text>
        <text x="442" y="258" class="node-sub">LLM-based routing</text>
      </g>

      <g :class="nodeClass('orchestrator')"
        @mouseenter="onEnter('orchestrator')" @mouseleave="onLeave"
        @click="onClick('orchestrator')">
        <rect x="575" y="210" width="210" height="70" rx="8" />
        <text x="680" y="232" class="node-title-sm">CodeOrchestrator</text>
        <text x="680" y="252" class="node-sub">ScriptGen \u2192 Roslyn</text>
        <text x="680" y="268" class="node-sub">\u2192 dotnet run</text>
      </g>

      <!-- ==================== ROW 4: Agent Cluster ==================== -->
      <g :class="nodeClass('infrastructure')"
        @mouseenter="onEnter('infrastructure')" @mouseleave="onLeave"
        @click="onClick('infrastructure')">
        <rect x="30" y="350" width="148" height="115" rx="6" />
        <text x="104" y="380" class="node-title-sm">Infrastructure</text>
        <text x="104" y="400" class="node-sub-sm">Shell \u00b7 FS \u00b7 Git</text>
        <text x="104" y="416" class="node-sub-sm">Aspire \u00b7 IAWSystem</text>
      </g>

      <g :class="nodeClass('csharp')"
        @mouseenter="onEnter('csharp')" @mouseleave="onLeave"
        @click="onClick('csharp')">
        <rect x="193" y="350" width="140" height="115" rx="6" />
        <text x="263" y="380" class="node-title-sm">C#</text>
        <text x="263" y="400" class="node-sub-sm">Roslyn \u00b7 DotNet</text>
        <text x="263" y="416" class="node-sub-sm">GitHub \u00b7 NuGet</text>
      </g>

      <g :class="nodeClass('memory')"
        @mouseenter="onEnter('memory')" @mouseleave="onLeave"
        @click="onClick('memory')">
        <rect x="348" y="350" width="140" height="115" rx="6" />
        <text x="418" y="380" class="node-title-sm">Memory</text>
        <text x="418" y="400" class="node-sub-sm">5 agents</text>
        <text x="418" y="416" class="node-sub-sm">Qdrant embeddings</text>
      </g>

      <g :class="nodeClass('llm')"
        @mouseenter="onEnter('llm')" @mouseleave="onLeave"
        @click="onClick('llm')">
        <rect x="503" y="350" width="148" height="115" rx="6" />
        <text x="577" y="380" class="node-title-sm">LLM Wrappers</text>
        <text x="577" y="400" class="node-sub-sm">14 model agents</text>
        <text x="577" y="416" class="node-sub-sm">Sonnet \u00b7 GPT \u00b7 \u2026</text>
      </g>

      <g :class="nodeClass('knowledge')"
        @mouseenter="onEnter('knowledge')" @mouseleave="onLeave"
        @click="onClick('knowledge')">
        <rect x="666" y="350" width="125" height="52" rx="6" />
        <text x="728" y="380" class="node-title-sm">Knowledge</text>
      </g>

      <g :class="nodeClass('userprofile')"
        @mouseenter="onEnter('userprofile')" @mouseleave="onLeave"
        @click="onClick('userprofile')">
        <rect x="666" y="413" width="125" height="52" rx="6" />
        <text x="728" y="443" class="node-title-sm">UserProfile</text>
      </g>

      <!-- ==================== ROW 5: Infrastructure ==================== -->
      <g :class="nodeClass('aspire')"
        @mouseenter="onEnter('aspire')" @mouseleave="onLeave"
        @click="onClick('aspire')">
        <rect x="35" y="520" width="230" height="55" rx="8" />
        <text x="150" y="542" class="node-title">Aspire + OTel</text>
        <text x="150" y="560" class="node-sub">Distributed orchestration</text>
      </g>

      <g :class="nodeClass('providers')"
        @mouseenter="onEnter('providers')" @mouseleave="onLeave"
        @click="onClick('providers')">
        <rect x="295" y="520" width="230" height="55" rx="8" />
        <text x="410" y="538" class="node-title">LLM Providers</text>
        <text x="410" y="558" class="node-sub">OpenAI \u00b7 Anthropic \u00b7 Ollama</text>
      </g>

      <g :class="nodeClass('state')"
        @mouseenter="onEnter('state')" @mouseleave="onLeave"
        @click="onClick('state')">
        <rect x="555" y="520" width="230" height="55" rx="8" />
        <text x="670" y="538" class="node-title">Durable State</text>
        <text x="670" y="558" class="node-sub">JournaledGrain + Qdrant</text>
      </g>
    </svg>

    <!-- Description Panel -->
    <div class="desc-panel" :class="{ 'desc-active': activeNode }">
      <template v-if="activeNode">
        <div class="desc-header">
          <h3>{{ nodes[activeNode].label }}</h3>
          <span v-if="lockedNode" class="desc-unlock">click to unlock</span>
        </div>
        <p>{{ nodes[activeNode].description }}</p>
        <a v-if="nodes[activeNode].link" :href="nodes[activeNode].link"
          class="desc-link">Learn more &rarr;</a>
      </template>
      <p v-else class="desc-default">
        &#x1F446; Hover any component to explore &middot; click to lock
      </p>
    </div>
  </div>
</template>

<style scoped>
.arch-wrapper {
  max-width: 900px;
  margin: 32px auto;
  overflow-x: auto;
}

.arch-svg {
  width: 100%;
  min-width: 580px;
  height: auto;
  display: block;
}

/* ---- Arrow styles ---- */
.arrow-line {
  stroke: var(--vp-c-divider);
  stroke-width: 1.5;
  fill: none;
}

.arrow-label {
  fill: var(--vp-c-text-3);
  font-family: var(--vp-font-family-base);
  font-size: 10px;
  text-anchor: middle;
  pointer-events: none;
}

.arrow-group {
  transition: opacity 0.2s ease;
}

.arrow-active .arrow-line {
  stroke: var(--vp-c-brand-1);
  stroke-width: 2;
}

.arrow-active .arrow-label {
  fill: var(--vp-c-brand-1);
}

.arrow-dimmed {
  opacity: 0.25;
}

/* ---- Marker fills ---- */
.marker-fill {
  fill: var(--vp-c-divider);
}

.marker-brand {
  fill: var(--vp-c-brand-1);
}

/* ---- Cluster frame ---- */
.cluster-bg {
  fill: var(--vp-c-bg-soft);
  opacity: 0.35;
}

.cluster-border {
  fill: none;
  stroke: var(--vp-c-divider);
  stroke-width: 1.5;
  stroke-dasharray: 6 4;
}

.cluster-label-bg {
  fill: var(--vp-c-bg);
  stroke: none;
}

.cluster-title {
  fill: var(--vp-c-text-3);
  font-family: var(--vp-font-family-base);
  font-size: 11px;
  font-weight: 500;
  text-anchor: middle;
  pointer-events: none;
}

/* ---- Node boxes ---- */
.node {
  cursor: pointer;
  transition: opacity 0.2s ease;
}

.node rect {
  fill: var(--vp-c-bg-soft);
  stroke: var(--vp-c-divider);
  stroke-width: 1.5;
  transition: stroke 0.2s ease, filter 0.2s ease;
}

.node-active rect {
  stroke: var(--vp-c-brand-1);
  stroke-width: 2;
  filter: drop-shadow(0 0 6px var(--vp-c-brand-soft));
}

.node-dimmed {
  opacity: 0.3;
}

/* ---- Node text ---- */
.node-title {
  fill: var(--vp-c-text-1);
  font-family: var(--vp-font-family-base);
  font-size: 13px;
  font-weight: 600;
  text-anchor: middle;
  pointer-events: none;
}

.node-title-sm {
  fill: var(--vp-c-text-1);
  font-family: var(--vp-font-family-base);
  font-size: 12px;
  font-weight: 600;
  text-anchor: middle;
  pointer-events: none;
}

.node-sub {
  fill: var(--vp-c-text-2);
  font-family: var(--vp-font-family-base);
  font-size: 10.5px;
  text-anchor: middle;
  pointer-events: none;
}

.node-sub-mono {
  fill: var(--vp-c-text-2);
  font-family: var(--vp-font-family-mono);
  font-size: 10.5px;
  text-anchor: middle;
  pointer-events: none;
}

.node-sub-sm {
  fill: var(--vp-c-text-3);
  font-family: var(--vp-font-family-base);
  font-size: 9.5px;
  text-anchor: middle;
  pointer-events: none;
}

/* ---- Description panel ---- */
.desc-panel {
  margin-top: 16px;
  padding: 16px 20px;
  border-radius: 8px;
  border: 1px solid var(--vp-c-divider);
  background: var(--vp-c-bg-soft);
  min-height: 72px;
  transition: border-color 0.2s ease;
}

.desc-active {
  border-color: var(--vp-c-brand-1);
}

.desc-header {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
}

.desc-header h3 {
  margin: 0 0 6px;
  font-size: 16px;
  font-weight: 600;
  color: var(--vp-c-text-1);
}

.desc-panel p {
  margin: 0;
  font-size: 14px;
  line-height: 1.6;
  color: var(--vp-c-text-2);
}

.desc-link {
  display: inline-block;
  margin-top: 8px;
  font-size: 13px;
  color: var(--vp-c-brand-1);
  text-decoration: none;
  font-weight: 500;
}

.desc-link:hover {
  text-decoration: underline;
}

.desc-unlock {
  font-size: 12px;
  color: var(--vp-c-text-3);
  white-space: nowrap;
}

.desc-default {
  text-align: center;
  color: var(--vp-c-text-3) !important;
  font-size: 14px;
  padding: 8px 0;
}
</style>
