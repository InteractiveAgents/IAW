# Agent V2 Redesign Plan (With UI Requirements and Findings)

## Context

This document captures:

- Findings from the current `IAgent`/`Agent` implementation.
- New UI requirements for Agent V2.
- A small-step migration plan for a full redesign from scratch.

Related audit document:

- `docs/plans/2026-03-04-agent-v2-method-audit.md`

## Findings

1. `IAgent` is too broad and combines many unrelated concerns in one interface.
2. `Agent` is monolithic and handles state, history, events, notifications, tracking, tools, streams, and LLM flow in one class.
3. Derived agents must pass six mandatory `[Memory(...)]` constructor arguments, creating high ceremony and tight coupling.
4. Some grain API methods expose infrastructure details that should be internal implementation concerns.
5. Method contracts rely heavily on string payloads and weak typing, making validation and evolution harder.
6. Docs, samples, and tests are tightly coupled to the current constructor shape and behavior composition.
7. Current behavior is stable and test-covered in core tests, which gives us a safe baseline for incremental migration.

## New UI Requirements

Note: "UI" here includes both end-user interaction UI and developer-facing API UI.

### End-User Interaction UI Requirements

1. Conversation-first interaction model (`request -> response`) with optional streaming responses.
2. Consistent message model across clients (Telegram, web, MCP, future channels).
3. Clear metadata support for message origin, timestamps, and routing context.
4. Stable user experience when optional capabilities are disabled (no hidden hard failures).
5. Mobile-friendly response chunks for chat channels that have length or rate limits.

### Developer API UI Requirements

1. No mandatory base constructor state arguments for custom agents.
2. Minimal core interface with small mental model and explicit defaults.
3. Optional capabilities split into focused interfaces/modules (not one god interface).
4. Stronger typed request/reply/message contracts and query objects.
5. Backward-compatible migration path with adapters and deprecations before removal.
6. Predictable naming: core terms should map directly to behavior (`Respond`, `Message`, `Memory`, `Profile`).
7. Clear extension points for tools, notifications, scheduling, and streaming without leaking implementation details.

### Operational UI Requirements

1. Preserve observability hooks for sends, failures, and tool usage.
2. Keep diagnostics and state-dump behavior outside core contracts.
3. Add explicit migration visibility via changelog/docs and `[Obsolete]` warnings.
4. Keep testability first-class with stable test harness behavior.

## Redesign Principles

1. Keep core small, make advanced behavior opt-in.
2. Prefer typed contracts over ad-hoc strings.
3. Remove plumbing from app code and keep infrastructure in runtime internals.
4. Maintain compatibility while V2 is introduced in parallel.
5. Ship in small, verifiable slices with test gates after each slice.

## Step-by-Step Plan

### Phase 0: Baseline and Guardrails

1. Freeze current behavior expectations in tests and architecture notes.
2. Keep core test suite green as migration safety net.
3. Track locked-process issues in local runs and use targeted test commands where needed.

### Phase 1: Additive V2 Contracts

1. Add `Core/V2` contracts (`IAgentV2`, request/reply/profile/message/query types).
2. Do not change runtime behavior in this phase.
3. Validate with core build + tests.

### Phase 2: Compatibility Adapter

1. Implement a V1->V2 adapter so existing `Agent` behavior can satisfy `IAgentV2`.
2. Keep all current `IAgent` endpoints functional.
3. Add adapter tests proving semantic parity for core flows.

### Phase 3: New Runtime and Base Class

1. Introduce runtime-managed state access abstraction for V2.
2. Build new `AgentV2` base class with no mandatory memory constructor args.
3. Move constructor/state plumbing behind runtime internals.

### Phase 4: Capability Extraction

1. Split optional concerns into dedicated V2 modules/interfaces:
2. Notifications/subscriptions.
3. Scheduling/tracking.
4. Streaming.
5. Tool invocation infrastructure.

### Phase 5: Pilot Migrations

1. Migrate `GitHubTestAgent` first (simple pilot).
2. Migrate `TelegramConversationGrain` second (complex real-world path).
3. Validate behavior parity and ergonomics.

### Phase 6: Docs, Samples, and Deprecation

1. Update README, guide pages, and samples to V2-first patterns.
2. Mark V1 API with `[Obsolete]` and provide migration guidance.
3. Keep dual-path support through one transition window.

### Phase 7: Major Cutover

1. Remove V1 contracts and legacy paths in next major release.
2. Finalize architecture guard tests for V2-only API.
3. Publish release notes with migration checklist.

## Validation Gates Per Phase

1. `dotnet build src/Core/Core.csproj`
2. `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --no-build`
3. Targeted integration checks for migrated consumers.
4. Docs/sample compile or smoke checks where applicable.

## Risks and Mitigations

1. Risk: API churn across many consumers.
2. Mitigation: adapter-first rollout and staged migrations.
3. Risk: hidden behavior regressions in notifications/tracking.
4. Mitigation: preserve old tests, add parity tests before extraction.
5. Risk: doc drift during transition.
6. Mitigation: update docs in each phase, not only at the end.

## Immediate Next Actions

1. Implement V1->V2 compatibility adapter in core.
2. Add parity tests for `RespondAsync`, message append/query, and memory set/get behavior.
3. Draft `AgentV2` runtime/state abstraction shape before any breaking changes.
