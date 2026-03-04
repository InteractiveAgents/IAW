# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

## [2.0.0] - 2026-03-04

### Breaking Changes
- Complete redesign of agent base class: `AgentV2` replaces `Agent`
- `IAgentV2` replaces the composed `IAgent` interface (8 behavior interfaces -> 1 flat interface)
- Derived agents no longer pass `[Memory]` constructor parameters
- V1 methods renamed: `AddHistoryAsync` -> `AppendMessageAsync`, `PublishEventAsync` -> `AppendEventAsync`, etc.
- Class renames: dropped `Agent`/`Grain` suffixes (TelegramConversation, AgentRouter, MonitorSourceProvider)

### Added
- `AgentV2` base class with runtime-managed state
- `IAgentV2` flat interface with profile, respond, messages, memory, events, notifications, scheduling, streaming, tools
- `AgentTestV2<T>` test base class with 16 universal behavior tests
- Full MCP orchestration API (8 tools)
- Voice transcription via OpenAI Whisper
- Complete documentation site rewrite

### Changed
- `Agent` now extends `AgentV2` (backward-compatible shim)
- `IAgent` now extends `IAgentV2`
- MCP tools use V2 API (GetProfileAsync, RespondAsync, etc.)

## [Unreleased]

### Added
- Unified `Agent` base class merging `OrleansAgentGrain` and internal `Agent` into a single `DurableGrain`-based public class
- Generic tools API (`DefineTools()` + `InvokeToolAsync`) replacing hardcoded tool methods
- LLM integration via `Microsoft.Extensions.AI` (`SendAsync` returning `IAsyncEnumerable<string>`)
- 8 behavior interfaces: Metadata, State, History, Events, Notifications, Tracking, Tools, Streams
- Telegram Bot client with webhook support and forum topic management
- VitePress documentation website
- GitHub Actions CI/CD pipeline
- Observability via OpenTelemetry (ActivitySource, counters)

### Removed
- `OrleansAgentGrain` (merged into `Agent`)
- `IAgentConfigurationBehavior` (over-engineered, dropped)
- `SendDeterministicAsync` (placeholder, replaced by real LLM `SendAsync`)
- All `OrleansAgent*` prefixed type names (renamed to clean names)
