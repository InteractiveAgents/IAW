# Orleans Grain Agent Migration Plan - Micro Steps

## Goal

Migrate agent functionality to Orleans grains, keeping each change as small as possible and validating each behavior in `samples/Samples` plus automated tests.

## Hard Rules

1. One behavior per step.
2. One commit per step.
3. Stop if a sample check fails.
4. Do not continue unless the related test passes.
5. Keep Orleans grains as the source of truth for agent state and behavior.

## Validation Gates

- Build gate: `dotnet build IAW.slnx`
- Sample gate: run request from `samples/Samples/AgentSamples.http`
- Test gate:
  - `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj --no-build`
  - `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build`

## Step Plan (From Scratch)

| Step | Change (smallest unit) | Sample validation | Automated validation |
| --- | --- | --- | --- |
| 0.1 | Add Orleans preview package versions to `Directory.Packages.props` | n/a | `dotnet build src/Core/Core.csproj` |
| 0.2 | Add `Aspire.Hosting.Orleans` to AppHost | n/a | `dotnet build src/IAW.AppHost/Aspire.csproj` |
| 0.3 | Add `AddIAW(...)` extension with memory storage/streams/reminders | n/a | `dotnet build src/IAW.AppHost/Aspire.csproj` |
| 0.4 | Wire AppHost to use `AddIAW` and pass gateway to clients | n/a | `dotnet build src/IAW.AppHost/Aspire.csproj` |
| 0.5 | Configure sample app Orleans host from `IAW:Orleans:*` settings | n/a | `dotnet build samples/Samples/Samples.csproj` |
| 1.1 | Make `IAgent` the primary Orleans grain contract; keep `IOrleansAgentGrain` as compatibility alias | n/a | `dotnet build src/Core/Core.csproj` |
| 1.2 | Add grain state DTOs/serializers | n/a | `dotnet build src/Core/Core.csproj` |
| 1.3 | Implement state set/get in `OrleansAgentGrain` | `GET /samples/orleans-agent/state` | `State_And_Increment_ArePersisted` |
| 1.4 | Implement metadata behavior | `GET /samples/orleans-agent/metadata` | `Metadata_ReturnsExpectedCapabilities` |
| 1.5 | Implement deterministic send + history | `GET /samples/orleans-agent/history` | `SendDeterministic_WritesHistory` |
| 1.6 | Implement event log behavior | `GET /samples/orleans-agent/events` | `Events_AreRecordedInOrder` |
| 1.7 | Implement subscriptions + notifications | `GET /samples/orleans-agent/notifications` | `Notify_DeliversToSubscribers` |
| 1.8 | Implement tracking with Orleans-native scheduling (reminders for durable >= 1m intervals, grain timers for sub-minute intervals) | `GET /samples/orleans-agent/tracking` | `Tracking_StartsTicks_AndStopsAtMax` |
| 1.9 | Implement runtime config patch/readback | `GET /samples/orleans-agent/configure` | `Configure_CanDisableResponsesAndTools` |
| 1.10 | Implement deterministic tool invocation | `GET /samples/orleans-agent/tool` | covered in config/tool tests |
| 1.11 | Implement stream publish API | n/a | `StreamPublish_IsReceivedByClientSubscription` |
| 1.12 | Add Orleans stream sample publish endpoint (manual harness) | `GET /samples/orleans-agent/stream` | `StreamPublish_IsReceivedByClientSubscription` |
| 1.13 | Migrate grain storage to `DurableGrain` + `[Memory(...)]` durable collections | existing Orleans sample endpoints | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj` |
| 1.14 | Align Orleans + Journaling package versions to a compatible preview set | n/a | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj` |
| 1.15 | Add notification behavior stream assertion | n/a | `Notify_EmitsAgentNotificationStream` |
| 1.16 | Migrate legacy `/samples/agent/state` endpoint to Orleans grain state | `GET /samples/agent/state` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.17 | Migrate legacy `/samples/agent/metadata` endpoint to Orleans grain metadata (legacy shape mapping) | `GET /samples/agent/metadata` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.18 | Migrate legacy `/samples/agent/tracking` endpoint to Orleans grain tracking (legacy shape mapping) | `GET /samples/agent/tracking` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.19 | Migrate legacy `/samples/agent/streaming` endpoint to Orleans streams (legacy shape mapping) | `GET /samples/agent/streaming` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.20 | Migrate legacy `/samples/agent/configure` endpoint to Orleans grain dynamic config/tools | `GET /samples/agent/configure` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.21 | Migrate legacy `/samples/agent/tool-call` endpoint to Orleans grain tool behavior | `GET /samples/agent/tool-call` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.22 | Migrate legacy `/samples/agent/events/publish` endpoint to Orleans grain events | `GET /samples/agent/events/publish` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.23 | Migrate legacy `/samples/agent/notifications` endpoint to Orleans grain notifications | `GET /samples/agent/notifications` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.24 | Migrate legacy `/samples/agent/history` endpoint to Orleans grain history | `GET /samples/agent/history` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.25 | Migrate legacy `/samples/agent/tools-custom` endpoint to Orleans grain tools | `GET /samples/agent/tools-custom` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.26 | Migrate legacy `/samples/agent/tools-default` endpoint to Orleans grain config/tools (legacy default behavior) | `GET /samples/agent/tools-default` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.27 | Migrate legacy `/samples/agent/identity` endpoint to Orleans grain metadata | `GET /samples/agent/identity` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.28 | Migrate legacy `/samples/agent/send-empty` endpoint to Orleans deterministic send with responses disabled | `GET /samples/agent/send-empty` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.29 | Migrate legacy `/samples/agent/system-prompt` endpoint to Orleans grain config metadata mapping | `GET /samples/agent/system-prompt` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.30 | Migrate legacy `/samples/agent/activate-default` endpoint to Orleans grain config mapping | `GET /samples/agent/activate-default` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.31 | Migrate legacy `/samples/agent/activate-custom` endpoint to Orleans grain config mapping with custom prompt | `GET /samples/agent/activate-custom` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.32 | Migrate legacy `/samples/agent/diagnose` endpoint to Orleans grain behavior snapshot | `GET /samples/agent/diagnose` | `OrleansSampleEndpoints_ReportExpectedBehavior` |
| 1.33 | Remove now-unused local sample helper agent classes after endpoint migration | n/a | `dotnet build IAW.slnx` |
| 2.1 | Create `test/Agents.Tests` skeleton (`TestCluster` + configurators) | n/a | `dotnet build test/Agents.Tests/IAW.Agents.Tests.csproj` |
| 2.2 | Add behavior tests one by one (state, metadata, history, events, notifications, tracking, config, streams) | n/a | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj --no-build` |
| 2.3 | Create `test/Integration.Tests` skeleton using `Aspire.Hosting.Testing` and AppHost reference | n/a | `dotnet build test/Integration.Tests/IAW.Integration.Tests.csproj` |
| 2.4 | Add cross-behavior integration test that starts the cluster through AppHost and validates Orleans sample endpoints (including streams/isolation) | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.5 | Add dedicated streaming behavior integration test (`/samples/agent/streaming`) with explicit ordered message assertions | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.6 | Upgrade `/samples/orleans-agent/stream` sample to verify client delivery, and add dedicated Orleans stream integration assertion | `GET /samples/orleans-agent/stream` | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.7 | Add E2E agent event-processing scenario (`/samples/orleans-agent/event-processing`) where one agent processes another agent's notification and persists processing result | `GET /samples/orleans-agent/event-processing` | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.8 | Add Aspire Testing endpoint-discovery E2E test that resolves `samples` URI via `GetEndpoint(...)` and runs agent event-processing scenario | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.9 | Switch event-processing scenario to Orleans Streams transport (`PublishStreamAsync` + stream subscription callback -> processor grain behavior), with completion verified from grain state | `GET /samples/orleans-agent/event-processing` | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.10 | Add direct Orleans client E2E integration test (Aspire-started cluster, no HTTP endpoint) validating stream-based producer->processor processing flow | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.11 | Add direct Orleans client single-publish stability test to assert no duplicate processing after one stream publish | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.12 | Add direct Orleans client dual-subscriber stream test to assert one publish is processed once by each subscriber agent | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.13 | Use Aspire Testing host Orleans endpoint resource in integration fixture (discover `orleans-gateway` via `GetEndpoint(...)`) and add explicit resource-availability test | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.14 | Add direct Orleans client negative stream test asserting one publish with no subscribers produces no processing side-effects | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.15 | Add direct Orleans client dual-subscriber ordered multi-message stream test asserting both subscribers process two messages in published order exactly once | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --no-build` |
| 2.16 | Ensure `IAgent` resolves to durable Orleans grain implementation by removing accidental `Core.Agent : IAgent` Orleans contract binding | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --filter OrleansClient_ --no-build` |
| 2.17 | Add Orleans sample state persistence integration test asserting repeated calls with the same `agentId` retain and increment grain state across requests | `GET /samples/orleans-agent/state?agentId=<fixed>` twice | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --filter OrleansStateEndpoint_SameAgentId_PersistsVisitCounterAcrossRequests --no-build` |
| 2.18 | Add direct Orleans client durability test asserting state and history persist for the same `agentId` across multiple calls | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --filter OrleansClient_StateAndHistory_PersistForSameAgentIdAcrossCalls --no-build` |
| 2.19 | Remove legacy in-memory channel streaming (`Streaming.cs` + local `Agent` channel methods) so Orleans streams are the only stream transport path | n/a | `dotnet build src/Core/Core.csproj --no-restore` |
| 2.20 | Re-enable full migration build gate by revalidating entire solution after streaming-path cleanup | n/a | `dotnet build IAW.slnx -v minimal` |
| 2.21 | Add architecture guard tests to prevent reintroduction of legacy local channel-streaming surface on `Core.Agent` and in `Core` assembly | n/a | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj --filter ArchitectureGuardTests --no-build` |
| 2.22 | Add typed Orleans notification envelope API (`OrleansAgentNotificationEnvelope`) with string-based notification wrappers for backward compatibility | n/a | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj --filter Notify_WithEnvelope_DeliversMetadataToSubscribers --no-build` |
| 2.23 | Add direct Orleans client integration test for notification envelope roundtrip, asserting subscriber receives envelope metadata persisted in notification records | n/a | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --filter OrleansClient_NotifyEnvelope_DeliversMetadataToSubscriber --no-build` |
| 2.24 | Add Orleans sample endpoint `/samples/orleans-agent/notifications-envelope` and endpoint-level integration assertion for envelope metadata | `GET /samples/orleans-agent/notifications-envelope` | `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj --filter OrleansNotificationsEnvelopeEndpoint_DeliversMetadata --no-build` |
| 2.25 | Add JSON helper API for notification envelopes (`OrleansAgentNotificationJson`) plus dynamic payload sample endpoint and typed roundtrip assertions | `GET /samples/orleans-agent/notifications-dynamic` | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj --filter Notify_WithJsonHelper_DeliversTypedPayloadToSubscriber --no-build` |
| 2.26 | Make legacy non-grain agent classes (`Core.Agent`, `Core.WeatherAgent`) non-public and add architecture guard to keep `IAgent` as the only public agent contract surface | n/a | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj --filter ArchitectureGuardTests --no-build` |
| 2.27 | Remove unused legacy `Core.WeatherAgent` class file and assert via architecture guard that no public/non-public `Core.WeatherAgent` type remains | n/a | `dotnet test test/Agents.Tests/IAW.Agents.Tests.csproj --filter ArchitectureGuardTests --no-build` |
| 3.1 | Add both test projects to `IAW.slnx` | n/a | `dotnet build IAW.slnx` |
| 3.2 | Keep `AgentSamples.http` aligned with Orleans endpoints only | run all requests in file | both test commands above |

## Current Execution Status (2026-02-27)

- Completed: 0.1 through 3.2 and 1.13 through 1.33, plus 2.5, 2.6, 2.7, 2.8, 2.9, 2.10, 2.11, 2.12, 2.13, 2.14, 2.15, 2.16, 2.17, 2.18, 2.19, 2.20, 2.21, 2.22, 2.23, 2.24, 2.25, 2.26, and 2.27.
- Remaining: optional hard delete of local non-grain `Agent` implementation file once no internal LLM helpers depend on it.
- Updated architecture: `IAgent` is grain-based and implemented by `OrleansAgentGrain : DurableGrain`; state/history/events/notifications/config/tracking use durable memories (`[Memory(...)]`), with reminder/timer hybrid tracking.
- Current validation: `dotnet build src/Core/Core.csproj` passing, `dotnet build IAW.slnx` passing, `Agents.Tests` (17/17, including `ArchitectureGuardTests` 3/3), `Integration.Tests --filter OrleansClient_` (8/8), and `Integration.Tests --filter FullyQualifiedName!~OrleansClient_` (9/9) passing.
