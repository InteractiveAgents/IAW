# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

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
