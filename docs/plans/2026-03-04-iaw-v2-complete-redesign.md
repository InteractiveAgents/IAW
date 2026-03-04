# IAW V2 Complete Redesign — Design Document

## Context

IAW is an Orleans 10.0-based multi-agent runtime shipping as both NuGet SDK packages and a ready-to-run platform (AppHost + Telegram bot + DevUI + MCP server). This document captures the full redesign plan to finish the project for open-source release.

Related documents:
- `docs/plans/2026-03-04-agent-v2-method-audit.md`
- `docs/plans/2026-03-04-agent-v2-redesign-plan-ui-findings.md`

## Decisions

| Decision | Choice |
|----------|--------|
| Release target | Both SDK (NuGet) + Platform (AppHost) |
| V1 migration | Clean break — drop V1 entirely |
| Agent V2 approach | Runtime-injected state, single flat AgentV2 class |
| Optional interfaces | None — everything in one AgentV2 class |
| Naming convention | Drop `Agent` and `Grain` suffixes (e.g. `PersonalAssistant`, `Shell`, `TelegramConversation`) |
| Telegram bot | Full assistant hub (voice, routing, monitors, topics) |
| MCP server | Full orchestration API (all 7+ tools, migrated to V2) |
| Priority order | Agent V2 → Telegram → MCP → Docs → Website |
| Pilot agents | PersonalAssistant, TelegramConversation, Shell, GitHubTest |
| Docs | Full VitePress site with guides, tutorials, API reference |

## 1. Agent V2 Core Architecture

### IAgentV2 Interface (Already Exists)

```csharp
public interface IAgentV2 : IGrainWithStringKey
{
    Task<AgentProfile> GetProfileAsync(CancellationToken ct = default);
    Task<AgentReply> RespondAsync(AgentRequest request, CancellationToken ct = default);
    Task AppendMessageAsync(AgentMessage message, CancellationToken ct = default);
    Task<List<AgentMessage>> QueryMessagesAsync(AgentMessageQuery? query = null, CancellationToken ct = default);
    Task SetMemoryAsync(string key, string value, CancellationToken ct = default);
    Task<string?> GetMemoryAsync(string key, CancellationToken ct = default);
}
```

Contracts in `src/Core/V2/`: AgentProfile, AgentRequest, AgentReply, AgentMessage, AgentMessageQuery.

### AgentV2 Base Class

```csharp
public abstract class AgentV2 : DurableGrain, IAgentV2, IRemindable
{
    // Runtime-managed state — derived agents never touch these
    [Memory("messages")] private readonly IDurableList<AgentMessage> _messages;
    [Memory("memory")] private readonly IDurableDictionary<string, string> _memory;
    [Memory("events")] private readonly IDurableList<AgentEvent> _events;
    [Memory("subscriptions")] private readonly IDurableDictionary<string, string> _subscriptions;
    [Memory("notifications")] private readonly IDurableList<NotificationRecord> _notifications;

    // Derived agents override:
    protected abstract AgentProfile Profile { get; }
    protected virtual Task<AgentReply> OnRespondAsync(AgentRequest request, CancellationToken ct);
    protected virtual IEnumerable<AITool> DefineTools() => [];

    // Built-in capabilities (all in one class, no optional interfaces):
    // - Message history (append, query)
    // - Key/value memory (set, get)
    // - Events (append, query)
    // - Notifications (subscribe, publish, receive, query inbox)
    // - Scheduling (start/stop timers via IRemindable)
    // - Streaming (Orleans stream publish)
    // - Tool invocation (via DefineTools + IChatClient)
    // - Observability (ActivitySource + meters)

    // Convenience accessors for derived agents:
    protected IDurableList<AgentMessage> Messages => _messages;
    protected IDurableDictionary<string, string> Memory => _memory;
    protected IDurableList<AgentEvent> Events => _events;
}
```

Key changes from V1 Agent:
- Derived agents never pass `[Memory]` constructor params — just override `Profile` and `OnRespondAsync`
- `SendAsync(string)` → `RespondAsync(AgentRequest)` with typed request/reply
- `Activate(IChatClient)` removed — LLM injected via `[Llm<TModel>]` constructor param
- All capabilities flat in one class (no IAgentNotificationsV2/IAgentSchedulingV2 split)
- Drop `Agent`/`Grain` suffix from all class names

### Naming Convention

| V1 Name | V2 Name |
|---------|---------|
| PersonalAssistantAgent | PersonalAssistant |
| ShellAgent | Shell |
| GitAgent | Git |
| FileSystemAgent | FileSystem |
| BuildAgent | Build |
| RoslynAgent | Roslyn |
| DotNetAgent | DotNet |
| NuGetAgent | NuGet |
| GitHubAgent | GitHub |
| ReviewerAgent | Reviewer |
| SelfImprovementAgent | SelfImprovement |
| KnowledgeAgent | Knowledge |
| UserAgent | User |
| PlanningAgent | Planning |
| NotificationAgent | Notification |
| DeployerAgent | Deployer |
| AspireAgent | Aspire |
| TelegramConversationGrain | TelegramConversation |
| AgentRouterGrain | AgentRouter |
| MonitorSourceProviderGrain | MonitorSourceProvider |
| GitHubTestAgent | GitHubTest |

### V2 Contracts (Already in src/Core/V2/)

- `AgentProfile` — Id, DisplayName, Instructions, Capabilities[]
- `AgentRequest` — Input, ConversationId, Metadata, TimestampUtc
- `AgentReply` — Output, ModelId, Metadata, TimestampUtc
- `AgentMessage` — MessageId, Role, Content, TimestampUtc, Metadata
- `AgentMessageQuery` — Limit, SinceUtc, Role, Descending

Additional V2 contracts needed:
- `AgentEvent` — EventId, Type, Payload, TimestampUtc, Metadata
- `AgentEventQuery` — Limit, SinceUtc, Type, Descending
- `ScheduleStatus` — IsRunning, Interval, TickCount, MaxTicks

## 2. Telegram Bot on V2

### Migration

- `TelegramConversation` extends `AgentV2` instead of `Agent`
- `ITelegramConversation` extends `IAgentV2` instead of `IAgent`
- All `SendAsync` calls → `RespondAsync`
- Voice transcription: finish Whisper integration
- Agent routing: `AgentRouter` calls `RespondAsync` on routed V2 agents
- Monitor sources: scheduling via `AgentV2` built-in timers

### Features to Complete

- Voice message pipeline (OGG → WAV → Whisper transcription → agent response)
- Monitor subscriptions (RSS, mock X/Reddit sources → periodic polling → notifications)
- Topic-based threading in group chats
- Inline keyboard interactions
- Error handling and graceful degradation

## 3. MCP Server on V2

### Tools to Implement (All Against IAgentV2)

| Tool | V2 Implementation |
|------|-------------------|
| `agent_list_all` | `GetProfileAsync` on well-known IDs |
| `assistant_chat` | `RespondAsync` on `personal-assistant` |
| `agent_send_message` | `RespondAsync` on any agent by ID |
| `agent_get_status` | `GetProfileAsync` + `QueryMessagesAsync` |
| `agent_assign_task` | `RespondAsync` with task metadata in request |
| `agent_get_events` | Query events from agent state |
| `agent_get_metrics` | Read observability/state data |
| `agent_trigger_self_improvement` | `RespondAsync` on `self-improvement` |

## 4. Documentation & Website

### VitePress Site Structure

```
website/
  index.md                          — Landing page (hero, features, quick example)
  guide/
    getting-started.md              — Install, first agent, run with Aspire
    architecture.md                 — Orleans, agents, LLM integration
    agent-v2.md                     — AgentV2 API reference and patterns
    telegram-bot.md                 — Setting up Telegram bot
    mcp-server.md                   — Claude Code integration via MCP
    deployment.md                   — Production deployment
    configuration.md                — AppHost config, LLM models, env vars
  reference/
    iagent-v2.md                    — IAgentV2 interface reference
    contracts.md                    — All V2 contract types
    samples.md                      — Guide to each sample project
    testing.md                      — AgentTest<T> and testing patterns
  tutorials/
    build-your-first-agent.md       — Step-by-step first agent
    multi-agent-orchestration.md    — Multi-agent patterns
    custom-tools.md                 — Adding tools to agents
    telegram-integration.md         — Building Telegram bot with IAW
```

### README

Complete rewrite for V2: quick install, minimal code example, feature list, architecture diagram, links to full docs.

## 5. System Refinements

- **CI/CD:** Fix `main` → `master` in workflows, add NuGet publish workflow, add integration test job
- **Package publishing:** NuGet metadata, package README, SemVer versioning
- **Testing:** Migrate `AgentTest<T>` to `AgentV2`, update all tests
- **Observability:** Wire `AgentObservability` into V2 base class
- **Samples:** Update all sample agents to V2 pattern and naming
- **DevUI:** Update to work with V2 agents
- **Global.json / Directory.Build.props:** Consistent SDK + package versions

## 6. V1 Removal

Clean break: delete all V1 types after V2 migration is complete.
- Remove `IAgent`, `IAgentBehaviors.cs`, old `Agent` base class
- Remove `AgentContracts.cs` (V1 contracts)
- Remove V1 test infrastructure from `AgentTest<T>`
- Update all references across codebase
