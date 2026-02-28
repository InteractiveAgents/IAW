# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/).

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
