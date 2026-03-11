# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [0.2.0] - 2026-03-11

### Added
- LLM agent hierarchy: 11 model agents (Sonnet46, Opus46, Claude45Haiku, Gpt4o, Gpt4oMini, Gpt52, Gpt53, Gemini31, GrokLatest, Llama32, Qwen25) each extending `IAW.Core.LLM` with `[Llm<TModel>]` injection and a dedicated `IXxx : IAgent` grain interface
- Memory agent hierarchy: 5 specialized memory agents (UserMemory, ProjectMemory, PatternMemory, EpisodeMemory, CodeMemory) extending `IAW.Core.Memory` with durable `IDurableList<MemoryEntry>`, `IEmbeddingGenerator`, and built-in Observe/Search/Consolidate/Decay/Forget operations
- Communication channels: task streams via `PublishToTaskStream<TEvent>(taskId, evt)` for scoped per-task event delivery, typed pub/sub via `IStreamProducer<TEvent>` / `IStreamConsumer<TEvent>` with automatic subscription on activation, and P2P messaging via `IReceiver<TMessage>` with accept/reject semantics
- `CodeOrchestratorAgent` with durable step tracking: `CreateTask`, `GetTaskState`, `PauseTask`, `ResumeTask` backed by JSON-serialized `TaskState` in durable grain state
- `TaskSupervisorAgent` for health monitoring: `RegisterTask`, `ReportProgress`, `GetTaskHealth`, `GetAllActiveTaskHealth` with stall detection via `TaskHealthRecord`
- `InterfaceCatalog` for reflection-based discovery of all `IAgent`-derived interfaces, computing grain IDs, and extracting communication contracts (`IStreamProducer<T>`, `IStreamConsumer<T>`, `IReceiver<T>`). Includes `ToPromptString()` for LLM-readable catalog output
- `OrchestrationCompiler` (Roslyn-based validation): `Compile(source)` parses generated orchestration scripts and returns `CompilationResult` with error diagnostics before execution
- `ScriptGenerator` converting `OrchestrationPlan` (sequence of `PlanStep` records) into runnable C# programs that connect to the Orleans cluster via `InterfaceCatalog` interface resolution
- `ScriptExecutor` for scaffolding temporary console projects and executing generated orchestration scripts with optional pre-validation
- Context providers: `MemoryContextProvider` (queries memory agents for relevant context) and `TaskStreamContextProvider` (extracts recent task events from agent event logs)
- Aspire hosting extensions: `WithCosmosStorage(cosmos)` for CosmosDB-backed grain storage and `WithQdrant(qdrant)` for Qdrant vector search integration, `WithLocalEmbeddings()` for local embedding generation
- `NotificationAgent` with channel routing: `SendNotification(request)` supporting Dashboard, Log, Telegram, Email channels with severity-based routing and `GetRecentNotifications(count)` for notification history
- `OrchestrationPlan` and `PlanStep` serializable records with `[GenerateSerializer]` for durable orchestration state
- `StepRecord` and `StepResult` for tracking individual orchestration step outcomes
- GitHub model variants: `GitHubGpt4o` and `GitHubGpt4oMini` with `ProviderType.GitHub`

### Changed
- Agent constructor now takes 5 parameters via primary constructor: `IDurableDictionary<string, StateEntry> state`, `IDurableList<AgentEvent> eventLog`, `IChatClient chatClient`, `IDurableList<ChatMessage> history`, `IDurableDictionary<string, TrackingItem> trackingItems`
- Auto-logging across all channels: `PublishAsync`, `PublishToStream<TEvent>`, and `PublishToTaskStream<TEvent>` all append to the durable event log with correlation IDs and publish to Orleans streams with OpenTelemetry activity spans and counter metrics
- `LLMModel.EnsureAllModelsLoaded()` expanded from 7 to 13 model registrations
- `AgentTestSiloConfigurator` registers all 13 LLM model mappers with `MockChatClient` and `MockEmbeddingGenerator`

## [0.1.0] - 2026-03-08

### Added
- Unified `Agent` base class extending `DurableGrain` with 8 partial files (Lifecycle, State, Events, Streams, Tools, Tracking)
- `IAgent` flat grain interface: 13 methods across conversation, state, metadata, events, streams, usage, lifecycle
- `[Memory("name")]` attribute for constructor injection of durable Orleans collections
- `[Llm<TModel>]` attribute for constructor injection of keyed `IChatClient` via `LlmAttributeMapper`
- 7 LLM model registrations: Sonnet46, Claude45Haiku, Gpt4o, Gpt4oMini, GitHubGpt4oMini, Qwen25, Llama32
- 14 built-in agents: FileSystem, Shell, Git, Build, Aspire, Roslyn, DotNet, NuGet, GitHub, Reviewer, SelfImprovement, Knowledge, User, Planning, Notification, Deployer
- PersonalAssistant CEO agent with task delegation, team status, and dynamic agent spawning tools
- `DynamicAgent` for runtime-created agents with configurable instructions
- P2P communication via `IReceiver<T>` with accept/reject semantics
- Pub/sub via `IStreamProducer<T>` / `IStreamConsumer<T>` over Orleans Streams
- Built-in tools: FileTools, ShellTools, WebTools, WorkspaceTools, WorkspaceFiles
- `AgentTest<T>` base class providing 18 universal behavior tests via Orleans TestingHost
- `AgentRegistrationStartupTask` for automatic agent discovery and registry population
- `UsageCaptureChatClient` for LLM token usage tracking
- MCP server bridge with 8 orchestration tools (agent_list_all, assistant_chat, etc.)
- DevUI integration via Microsoft Agent Framework
- .NET Aspire hosting: `AddIAW()`, `WithLLM<T>()`, `WithLLMEnvironment()`
- 3 sample Orleans client apps: SimpleClient, Pipeline, Monitor
- 89 unit tests + architecture guard tests
- OpenTelemetry observability (ActivitySource, counters)

### Removed
- `IBroadcaster<T>`, `INotifier<T>`, `IAgentObserver` (cut for v0.x — use IReceiver + Streams)
- `BroadcastResult` (dead code)
- V1/V2 agent models and namespaces

## [Unreleased]
