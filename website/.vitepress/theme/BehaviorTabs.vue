<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed } from 'vue'

interface Behavior {
  key: string
  label: string
  icon: string
  description: string
  code: string
}

const behaviors: Behavior[] = [
  {
    key: 'state',
    label: 'State',
    icon: '💾',
    description: 'Durable key-value store with atomic counters — survives restarts.',
    code: `var agent = grains.GetGrain<IAgent>("weather");

await agent.SetStateAsync("city", "Seattle");
var visits = await agent.IncrementAsync("visits");

var state = await agent.GetStateAsync();
// { "city": "Seattle", "visits": "3" }`
  },
  {
    key: 'history',
    label: 'History',
    icon: '💬',
    description: 'Conversation history with role tracking and timestamps.',
    code: `var agent = grains.GetGrain<IAgent>("assistant");

await agent.AddHistoryAsync("user", "What's the weather?");
await agent.AddHistoryAsync("assistant", "72°F and sunny in Seattle.");

var history = await agent.GetHistoryAsync();
// [{ Role: "user", ... }, { Role: "assistant", ... }]`
  },
  {
    key: 'events',
    label: 'Events',
    icon: '📋',
    description: 'Publish events with payloads — automatic audit trail and streaming.',
    code: `var agent = grains.GetGrain<IAgent>("monitor");

await agent.PublishEventAsync("deployment.started",
    """{"service": "api", "version": "2.1.0"}""");

var events = await agent.GetEventsAsync();
// Each event: { Name, Payload, TimestampUtc }`
  },
  {
    key: 'notifications',
    label: 'Notifications',
    icon: '🔔',
    description: 'Pub/sub between agents — subscribe to topics and broadcast messages.',
    code: `var alerts = grains.GetGrain<IAgent>("alerts");

await alerts.SubscribeAsync("system.alert", "dashboard");
await alerts.NotifyAsync("system.alert",
    """{"level": "critical", "cpu": "98%"}""");

// "dashboard" agent receives the notification automatically`
  },
  {
    key: 'tools',
    label: 'Tools',
    icon: '🔧',
    description: 'Define AI functions that LLMs can invoke during conversations.',
    code: `public override IReadOnlyList<AITool> DefineTools() =>
[
    AIFunctionFactory.Create((string city) =>
        $"72°F and sunny in {city}",
        "get_weather",
        "Get current weather for a city")
];

// LLM calls tools automatically during SendAsync`
  },
  {
    key: 'streams',
    label: 'Streams',
    icon: '🌊',
    description: 'Real-time data flow via Orleans streams with custom namespaces.',
    code: `var sensor = grains.GetGrain<IAgent>("sensor");
var streamId = Guid.NewGuid();

await sensor.PublishStreamAsync(
    "telemetry", streamId,
    """{"temp": 72.5, "humidity": 0.65}""");

// All subscribers on "telemetry" stream receive instantly`
  },
  {
    key: 'tracking',
    label: 'Tracking',
    icon: '⏱️',
    description: 'Periodic timer execution with configurable intervals and auto-stop.',
    code: `var poller = grains.GetGrain<IAgent>("poller");

// Tick every 30 seconds, auto-stop after 100 ticks
await poller.StartTrackingAsync(
    TimeSpan.FromSeconds(30), maxTicks: 100);

var status = await poller.GetTrackingStatusAsync();
// { IsTracking: true, TickCount: 42, ... }`
  },
  {
    key: 'metadata',
    label: 'Metadata',
    icon: '🪪',
    description: 'Agent identity, display name, and capability discovery.',
    code: `var agent = grains.GetGrain<IAgent>("assistant");

var meta = await agent.GetMetadataAsync();
// {
//   Id: "assistant",
//   DisplayName: "Personal Assistant",
//   Capabilities: ["state", "history", "events",
//     "notifications", "tracking", "streams", "tools"]
// }`
  }
]

const activeIndex = ref(0)
const progress = ref(0)
const INTERVAL = 15000
const TICK = 50

let timer: ReturnType<typeof setInterval> | null = null

function startTimer() {
  stopTimer()
  progress.value = 0
  timer = setInterval(() => {
    progress.value += (TICK / INTERVAL) * 100
    if (progress.value >= 100) {
      activeIndex.value = (activeIndex.value + 1) % behaviors.length
      progress.value = 0
    }
  }, TICK)
}

function stopTimer() {
  if (timer) {
    clearInterval(timer)
    timer = null
  }
}

function selectTab(index: number) {
  activeIndex.value = index
  progress.value = 0
  startTimer()
}

const activeBehavior = computed(() => behaviors[activeIndex.value])

onMounted(() => startTimer())
onUnmounted(() => stopTimer())
</script>

<template>
  <section class="behavior-tabs">
    <div class="behavior-tabs-header">
      <h2>Composable Agent Behaviors</h2>
      <p>Every agent inherits 8 durable behaviors out of the box. Mix, override, and extend.</p>
    </div>
    <div class="behavior-tabs-container">
      <div class="tab-list" role="tablist">
        <button
          v-for="(b, i) in behaviors"
          :key="b.key"
          role="tab"
          :aria-selected="i === activeIndex"
          :class="['tab-item', { active: i === activeIndex }]"
          @click="selectTab(i)"
        >
          <span class="tab-icon">{{ b.icon }}</span>
          <span class="tab-label">{{ b.label }}</span>
        </button>
        <div class="tab-progress">
          <div class="tab-progress-bar" :style="{ width: progress + '%' }" />
        </div>
      </div>
      <div class="tab-panel" role="tabpanel">
        <div class="panel-description">{{ activeBehavior.description }}</div>
        <div class="panel-code">
          <div class="code-header">
            <span class="code-lang">csharp</span>
          </div>
          <pre><code>{{ activeBehavior.code }}</code></pre>
        </div>
      </div>
    </div>
  </section>
</template>

<style scoped>
.behavior-tabs {
  max-width: 1152px;
  margin: 0 auto;
  padding: 64px 24px;
}

.behavior-tabs-header {
  text-align: center;
  margin-bottom: 48px;
}

.behavior-tabs-header h2 {
  font-size: 32px;
  font-weight: 700;
  letter-spacing: -0.02em;
  line-height: 1.2;
  margin: 0 0 12px;
}

.behavior-tabs-header p {
  font-size: 16px;
  color: var(--vp-c-text-2);
  margin: 0;
}

.behavior-tabs-container {
  display: flex;
  gap: 24px;
  align-items: stretch;
}

.tab-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 180px;
  flex-shrink: 0;
}

.tab-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 16px;
  border: none;
  background: transparent;
  border-radius: 8px;
  cursor: pointer;
  font-size: 15px;
  font-weight: 500;
  color: var(--vp-c-text-2);
  text-align: left;
  transition: all 0.2s ease;
  font-family: var(--vp-font-family-base);
}

.tab-item:hover {
  color: var(--vp-c-text-1);
  background: var(--vp-c-bg-soft);
}

.tab-item.active {
  color: var(--vp-c-brand-1);
  background: var(--vp-c-brand-soft);
  font-weight: 600;
}

.tab-icon {
  font-size: 18px;
  width: 24px;
  text-align: center;
  flex-shrink: 0;
}

.tab-progress {
  margin-top: 8px;
  height: 3px;
  background: var(--vp-c-divider);
  border-radius: 3px;
  overflow: hidden;
}

.tab-progress-bar {
  height: 100%;
  background: var(--vp-c-brand-1);
  border-radius: 3px;
  transition: width 50ms linear;
}

.tab-panel {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.panel-description {
  font-size: 16px;
  color: var(--vp-c-text-2);
  line-height: 1.5;
  padding: 0 4px;
}

.panel-code {
  border-radius: 12px;
  overflow: hidden;
  background: var(--vp-code-block-bg);
  border: 1px solid var(--vp-c-divider);
}

.code-header {
  display: flex;
  justify-content: flex-end;
  padding: 8px 16px 0;
}

.code-lang {
  font-size: 12px;
  font-weight: 500;
  color: var(--vp-c-text-3);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.panel-code pre {
  margin: 0;
  padding: 16px 24px 24px;
  overflow-x: auto;
}

.panel-code code {
  font-family: var(--vp-font-family-mono);
  font-size: 14px;
  line-height: 1.7;
  color: var(--vp-c-text-1);
  white-space: pre;
}

@media (max-width: 768px) {
  .behavior-tabs {
    padding: 48px 16px;
  }

  .behavior-tabs-header h2 {
    font-size: 24px;
  }

  .behavior-tabs-container {
    flex-direction: column;
    gap: 16px;
  }

  .tab-list {
    flex-direction: row;
    min-width: 0;
    overflow-x: auto;
    gap: 4px;
    padding-bottom: 4px;
    -webkit-overflow-scrolling: touch;
  }

  .tab-item {
    white-space: nowrap;
    padding: 8px 12px;
    font-size: 14px;
  }

  .tab-label {
    display: none;
  }

  .tab-icon {
    font-size: 20px;
  }

  .tab-progress {
    display: none;
  }

  .panel-code pre {
    padding: 12px 16px 16px;
  }

  .panel-code code {
    font-size: 13px;
  }
}
</style>
