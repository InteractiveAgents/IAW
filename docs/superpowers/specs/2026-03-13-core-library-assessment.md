# IAW Core Library Assessment

**Date:** 2026-03-13
**Target:** Public NuGet + commercial product
**Breaking changes:** Full freedom (v3)
**Scope:** 360-degree subsystem-by-subsystem audit

---

## 1. Agent Base Class (8 partial files)

### Strengths
- Partial-class decomposition by behavioral concern (Events, Streams, State, Lifecycle, Tools, Tracking, Observers) is clean and navigable.
- Journaling via `IDurableDictionary`/`IDurableList` is efficient — delta-based persistence with event-sourcing for free.
- `EnrichWithContext` pipeline is composable — agents opt into context providers without the base class knowing specifics.
- Override points (`DefineTools()`, `GetContextProviders()`, `Instructions`, `DisplayName`) are well-chosen.

### Weaknesses
- **Constructor has 5 injected parameters** — every agent forwards all 5 `[Memory]` + `[Llm]` params. Adding a 6th breaks all consuming projects. Dealbreaker for public NuGet.
- **`OnActivateAsync` monolith** — wraps chat client, builds AIAgent, creates session, auto-subscribes streams, re-registers reminders. No partial recovery if any step fails. No template methods for customization.
- **Silent swallowing in `EnrichWithContext`** — context provider failures are caught and ignored. Hides real problems during development.
- **No history size management** — `IDurableList<ChatMessage>` grows forever. No truncation, sliding window, or summarization.
- **No circuit breaker or retry on LLM calls** — provider downtime surfaces as unhandled exceptions.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Collapse constructor to a single `AgentServices` bag injected by Orleans | Yes | Critical |
| 2 | Extract `OnActivateAsync` into template methods (`OnConfigureConversation`, `OnConfigureInfrastructure`) | Yes | High |
| 3 | Add history truncation strategy (configurable max messages, sliding window) | No | High |
| 4 | Log swallowed exceptions in `EnrichWithContext` | No | Medium |
| 5 | Add virtual `CreateAgentOptions()` so subclasses can customize `ChatClientAgentOptions` | No | Medium |

---

## 2. IAgent Interface

### Strengths
- Flat interface — simple to discover and understand.
- Every method takes `CancellationToken`.
- `IGrainWithStringKey` is the right base for human-readable agent IDs.
- Derived interfaces (`ISonnet46 : IAgent`, `IFileSystem : IAgent`) enable type-safe grain references with `InterfaceCatalog` auto-discovery.

### Weaknesses
- **11 methods spanning 7 concerns** — conversation, state, metadata, events, streams, usage, lifecycle. Violates ISP. Consumer implementing a conversation-only agent must implement all 11.
- **`GetHistory` and `GetEventLog` return unbounded lists** — full serialization over the wire for large histories.
- **`GetActiveSubscriptions` returns raw strings** — leaks internal naming convention.
- **No versioning strategy** — adding a method to `IAgent` breaks all consumer implementations.
- **No `GetCumulativeUsage()`** — only last-call usage, insufficient for billing.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Split `IAgent` into focused interfaces (`IConversation`, `IObservable`, `IConfigurable`) with `IAgent` composing them | Yes | Critical |
| 2 | Add pagination to `GetHistory` and `GetEventLog` (`int skip, int take`) | Yes | High |
| 3 | Replace `GetActiveSubscriptions` string return with typed `StreamSubscription` record | Yes | Medium |
| 4 | Add `GetCumulativeUsage()` for billing scenarios | No | Medium |

---

## 3. Memory System

### Strengths
- Clean abstract base — `Observe`, `Search`, `Decay`, `Forget`, `Consolidate` are the right primitives.
- `MemoryProvenance` tracks source, task, agent, trust score, conversation — audit-ready.
- `Decay(factor)` for relevance fading — most frameworks skip this.
- `IEmbeddingGenerator` injected but not forced — subclasses can upgrade to semantic search.

### Weaknesses
- **`Search` is linear scan with `string.Contains`** — O(n) per query, no indexing. Embedding generator available but unused in base class.
- **No deduplication** — `Observe` always appends. Replayed observations create duplicates.
- **`ForgetAsync` matches by content equality, not ID** — loses both entries if content is identical with different provenance. Internal `Forget(memoryId)` uses ID but isn't exposed on `IMemoryAgent`.
- **`SupersededBy` field is never set** — dead field.
- **`Search` mutates state (AccessCount, LastAccessedAt)** — read operation triggers `WriteStateAsync`. Write amplification under read-heavy workloads.
- **No eviction** — `Decay` reduces scores but never removes entries. No max-size cap.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Implement semantic search in base `Memory` using injected `IEmbeddingGenerator` | Yes | Critical |
| 2 | Add deduplication in `Observe` — content hash check | No | High |
| 3 | Add eviction policy — max entry count with lowest-relevance eviction | No | High |
| 4 | Separate access tracking from `Search` — opt-in or batch writes | No | High |
| 5 | Expose `Forget(memoryId)` on `IMemoryAgent` | Yes | Medium |
| 6 | Add `MemoryCategory` enum or tag system to `MemoryEntry` | Yes | Medium |
| 7 | Wire `SupersededBy` or remove the dead field | Yes | Low |

---

## 4. LLM Integration

### Strengths
- Self-registering model singletons with `EnsureAllModelsLoaded()` — no config-driven discovery bugs.
- `LlmAttribute<TModel>` + `LlmAttributeMapper<TModel>` piggybacks Orleans' `IFacetMetadata` cleanly.
- `ChatClientBuilder` pipeline (`UseStreamingUsage` + `UseOpenTelemetry`) follows Microsoft.Extensions.AI patterns.
- Mappers registered for all models (not just configured ones) — grains fail at resolution time with clear errors.

### Weaknesses
- **Closed model registry** — 13 hardcoded sealed classes. NuGet consumers can't add custom models (fine-tuned, local LLM, new provider). `EnsureAllModelsLoaded` only discovers Core assembly models. Showstopper for public NuGet.
- **`ProviderType` is a closed enum** — adding Azure OpenAI, Bedrock, Vertex requires modifying Core.
- **`LlmRegistration.AddLlmProviders()` has hardcoded `switch` on provider** — new providers require modifying the switch.
- **No resilience** — no retry, no circuit breaker. Transient 429/503 surfaces as unhandled exception.
- **No rate limiting or concurrency control** — all agents share the same `IChatClient` singleton per model.
- **`UsageCaptureChatClient` only captures non-streaming usage** — streaming gap for non-OpenAI providers.
- **`ModelCapabilities` exists but is never checked** — agent with tools on a model that doesn't support tools fails at runtime, not startup.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Open model registration — config-driven model definitions without class-per-model requirement | Yes | Critical |
| 2 | Replace `ProviderType` enum with string-based registry + `ILlmProviderFactory` interface | Yes | Critical |
| 3 | Add resilience middleware — retry with backoff, circuit breaker via Polly | No | High |
| 4 | Validate `ModelCapabilities` at startup — fail fast if agent declares tools on a non-tool-capable model | No | High |
| 5 | Add `ServiceKey` collision detection at registration time | No | Medium |
| 6 | Add `ChatClientBuilder` customization hook | No | Medium |
| 7 | Fix streaming usage capture for non-OpenAI providers | No | Medium |

---

## 5. Events & Streams

### Strengths
- Three publishing paths (untyped `PublishAsync`, typed `PublishToStream<T>`, task-scoped `PublishToTaskStream<T>`) cover real use cases.
- `EventTypeToStreamName` auto-derives stream names from type names — eliminates wiring bugs.
- `IStreamConsumer<T>` auto-subscription on activation — implementing the interface is all an agent needs.
- Architecture guards enforce `T : IEvent` constraint on stream interfaces.

### Weaknesses
- **Untyped `PublishAsync` has no discoverability** — raw string event names with no compile-time validation, no catalog. Infrastructure agents still use this path heavily.
- **No delivery guarantees** — memory streams are at-most-once. Events lost if subscriber is deactivated.
- **Stream subscriptions don't survive silo restarts** — only fire on activation. Events before activation are lost.
- **`AgentEvent` is overloaded** — serves as both untyped envelope and typed wrapper. Typed payload stored as `object` in a dictionary — loses type safety at serialization boundary.
- **Event log is append-only with no compaction** — grows without bound.
- **Both stream publishing and event logging happen per action** — doubles write cost for high-frequency events.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Migrate untyped `PublishAsync` callsites to typed `PublishToStream<T>` — deprecate untyped path | Yes | Critical |
| 2 | Add event log rotation — configurable max size with oldest-first eviction | No | High |
| 3 | Decouple event log from stream publishing — make logging opt-in per event type | No | High |
| 4 | Document and test with persistent stream provider for production | No | High |
| 5 | Add subscription deduplication on reactivation | No | Medium |
| 6 | Replace `object` typed payload in `AgentEvent` with proper `IEvent`-typed envelope | Yes | Medium |

---

## 6. Orchestration

### Strengths
- `InterfaceCatalog.Discover()` auto-discovers all `IAgent`-derived interfaces and maps to grain IDs — always in sync with codebase.
- `ToPromptString()` rendering catalog as Markdown for LLM injection is a practical pattern.
- `ComputeGrainId` naming convention is consistent and predictable.

### Weaknesses
- **Code generation as orchestration** — `ScriptGenerator` produces C# that must compile against correct packages and connect to the right silo. Brittle, hard to maintain.
- **`ScriptExecutor` shells out to `dotnet run`** — subprocess with no shared context, no cancellation propagation, no streaming results, cold start overhead, temp directory accumulation.
- **No orchestration runtime** — no pause/resume, no step failure handling, no parallel execution, no real-time progress without polling.
- **`InterfaceCatalog.Discover()` is AppDomain-based** — only scans loaded assemblies, non-deterministic results.
- **`ComputeGrainId` assumes single instance per type** — no multi-instance awareness.
- **`OrchestrationPlan` is a flat list** — no dependency graph, no conditional branching, no parallel groups, no error handling.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Build in-process `OrchestrationRunner` — execute plans as direct grain calls, retire `ScriptExecutor` for production | Yes | Critical |
| 2 | Upgrade `OrchestrationPlan` to DAG — dependencies, parallel groups, conditionals, retry policies | Yes | Critical |
| 3 | Make `InterfaceCatalog.Discover()` deterministic — scan referenced assemblies or require explicit registration | Yes | High |
| 4 | Add multi-instance awareness to catalog — `(interfaceType, grainId)` tuples | Yes | High |
| 5 | Keep `ScriptGenerator` + `ScriptExecutor` as dev/debug tool only | No | Medium |

---

## 7. Tools

### Strengths
- `WebTools` has comprehensive SSRF protection — scheme check, hostname blocklist, private IP blocking, async DNS resolution for TOCTOU defense.
- `WorkspaceFiles` is git-aware with `git ls-files` fallback to manual walk.
- `FileTools.ValidatePathWithinWorkspace` prevents path traversal.
- Tool discovery via `[Description]` + `AIFunctionFactory.Create` follows Microsoft.Extensions.AI conventions.

### Weaknesses
- **Tools live in the base `Agent` class** — every agent gets `FileTools` and `ShellTools` once workspace is set. No opt-out. Violates least privilege.
- **`ShellTools` allows arbitrary command execution** — workspace sandbox restricts working directory only, not what commands run. No allowlisting.
- **`ShellTools` timeout (120s) and output cap (8KB) are hardcoded** — not configurable.
- **`FileTools` exclusion list is hardcoded** — consumers with different conventions can't customize.
- **No tool permission model** — tools are either available (workspace set) or not. No granularity for read-only, command restrictions, or user preferences.

### Workspace Direction
`SetWorkspace` stays on `IAgent` — workspace is a core concept (the folder IAW agents can access). File/shell tool ownership and access policy enforcement are open design questions for the implementation plan.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Remove `FileTools` and `ShellTools` from base `Agent.GetAllTools()` — make opt-in | Yes | Critical |
| 2 | Add tool permission/capability model driven by configuration or user preferences | Yes | Critical |
| 3 | Add command allowlisting to `ShellTools` | No | High |
| 4 | Make `ShellTools` timeout and output cap configurable | No | High |
| 5 | Make `FileTools` exclusion patterns configurable | No | Medium |
| 6 | Add tool invocation middleware for authorization, audit, rate limiting | No | Medium |

---

## 8. Registry

### Strengths
- Auto-discovery on silo startup via reflection — no manual registration.
- Durable grain — registrations survive restarts.
- `AgentQuery` filters by `Kind`, `Publishes[]`, `Subscribes[]`.

### Weaknesses
- **Type-based, not instance-based** — knows "FileSystemAgent exists" but not "fs-1, fs-2, fs-3 are active."
- **`DynamicAgent` instances are never registered** — invisible to the registry.
- **Duplicate registration on every restart** — unnecessary I/O.
- **Single `"global"` grain** — no namespace, no tenant isolation.
- **`AgentRegistration` is thin** — no version, no health, no capabilities.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Add instance-level registration — track `(type, grainId)` pairs | Yes | High |
| 2 | Register `DynamicAgent` instances on `ConfigureAsync` | No | High |
| 3 | Skip re-registration if unchanged | No | Medium |
| 4 | Enrich `AgentRegistration` with capabilities and version | Yes | Medium |
| 5 | Consider namespace/sharding for multi-tenant | Yes | Future |

---

## 9. Testing Framework

### Strengths
- `AgentTest<T>` is zero-friction — inherit and get full `TestCluster` with mocks.
- `UniqueId(prefix)` prevents state bleed between test classes.
- `Agent(id)` helper resolves the most-specific interface via reflection.
- Test agent variants cover every communication pattern.

### Weaknesses
- **No per-test LLM behavior** — `MockChatClient.ReturnsText` set once during cluster setup. Can't simulate different responses across tests.
- **`MockChatClient` doesn't capture calls** — returns canned response but doesn't record prompts, tools invoked, or call count.
- **No assertion helpers** — no `ShouldHavePublished<T>()`, no `WaitForStreamEvent<T>(timeout)`.
- **No failure-mode mocks** — can't simulate timeouts, errors, or malformed responses.
- **No silo configuration customization** — consumers can't extend `AgentTestSiloConfigurator` without copying everything.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Make `MockChatClient` capture calls — expose `Calls` list for assertions | No | High |
| 2 | Allow per-test LLM behavior — `ReturnsSequence(...)` or `Returns(Func<prompt, response>)` | No | High |
| 3 | Add assertion helpers for events, LLM calls, stream events | No | High |
| 4 | Allow silo configuration customization via virtual method or `Action<ISiloBuilder>` | No | Medium |
| 5 | Add failure-mode mocks — `Throws<T>()`, `TimesOut()`, `ReturnsEmpty()` | No | Medium |

---

## 10. Session Management

### Strengths
- `DurableChatHistoryProvider` bridges Orleans journaling to Microsoft.Agents.AI cleanly.
- `ClearHistory` correctly resets both durable list and session.
- `DynamicAgent` config persists in durable state — survives deactivation and restarts.

### Weaknesses
- **No session concept beyond one-per-grain** — no multi-user, no session switching. `ClearHistory` destroys everything.
- **`StoreChatHistoryAsync` appends without deduplication** — retries create duplicates.
- **No history windowing** — `ProvideChatHistoryAsync` loads entire durable list on every LLM call. 5,000-message agent serializes all 5,000 objects every turn.
- **`DynamicAgent.ConfigureAsync` doesn't reset session** — new instructions only take effect after grain reactivation.
- **`DynamicAgent.ToolNames` is persisted but never resolved to tools** — dead config.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Add history windowing — configurable max messages or tokens, oldest dropped or summarized | No | Critical |
| 2 | Add multi-session support — `GetResponse(prompt, sessionId?, ct)` | Yes | Critical |
| 3 | Make history provider injectable or virtual | Yes | High |
| 4 | Fix `DynamicAgent.ConfigureAsync` to rebuild AIAgent with new instructions | No | High |
| 5 | Remove or wire `DynamicAgent.ToolNames` | Yes | Medium |
| 6 | Add message deduplication in `StoreChatHistoryAsync` | No | Medium |

---

## 11. Observability & Telemetry

### Strengths
- Dedicated `ActivitySource` and `Meter` named `"IAW"` — clean namespace for OTel backends.
- Five counters + two histograms cover key operational signals.
- Every LLM call wrapped in OTel activity — distributed traces flow through agent-to-agent calls.

### Weaknesses
- **No per-agent metrics** — no tags for agent type or ID. Aggregate-only counters are useless for debugging at scale.
- **No cumulative LLM cost tracking** — `GetLastUsage` overwrites on each call. No aggregation, no persistence.
- **No error classification** — all errors in one counter. No distinction between provider, tool, context, or application errors.
- **No latency breakdown** — full `GetResponse` duration only. Can't see context enrichment vs. LLM call vs. state persistence.
- **`UseStreamingUsage` only works for OpenAI-compatible providers** — Anthropic ignores or errors on `stream_options`.
- **No agent-specific health checks**.

### Recommendations

| # | Change | Breaking? | Severity |
|---|--------|-----------|----------|
| 1 | Add `agent.type` and `agent.id` tags to all counters and histograms | No | Critical |
| 2 | Add cumulative usage tracking — persist token counts, expose `GetCumulativeUsage()` | Yes | High |
| 3 | Add error classification tags on errors counter | No | High |
| 4 | Add latency breakdown — separate spans for enrichment, LLM, persistence | No | Medium |
| 5 | Make `UseStreamingUsage` provider-aware | No | Medium |
| 6 | Add agent-specific health checks | No | Medium |

---

## Cross-Cutting Priority Matrix

| Priority | Issue | Subsystems |
|----------|-------|------------|
| **Critical** | Constructor parameter bag — adding a 6th breaks all consumers | Agent base |
| **Critical** | History grows unbounded, loads fully on every LLM call | Session, Agent base |
| **Critical** | Closed model/provider registry — consumers can't add their own | LLM |
| **Critical** | No in-process orchestration runtime | Orchestration |
| **Critical** | Aggregate-only telemetry — no per-agent visibility | Observability |
| **Critical** | Semantic memory search not implemented in base | Memory |
| **Critical** | Every agent gets shell/file tools by default | Tools |
| **Critical** | Untyped `PublishAsync` with string event names | Events |
| **Critical** | `OrchestrationPlan` is flat list, not DAG | Orchestration |
| **High** | `IAgent` too broad — 11 methods, no segmentation | Interface |
| **High** | No multi-session support | Session |
| **High** | No LLM resilience (retry, circuit breaker) | LLM |
| **High** | Registry tracks types not instances, misses DynamicAgent | Registry |
| **High** | MockChatClient doesn't capture calls or support per-test behavior | Testing |
| **High** | Event log grows unbounded | Events |
| **High** | `DynamicAgent.ConfigureAsync` doesn't reset session | Session |
| **High** | No `OnActivateAsync` template methods for customization | Agent base |
