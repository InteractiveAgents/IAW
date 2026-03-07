# Opensource Readiness — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Prepare IAW V3 for public opensource release — API surface review, documentation, samples, CI/CD, licensing, security audit, NuGet packaging, and VitePress website update.

**Architecture:** IAW ships as NuGet packages (IAW.Core, IAW.Hosting, IAW.Agents, IAW.Testing) + Aspire AppHost. Documentation via VitePress. CI/CD via GitHub Actions.

**Tech Stack:** .NET 11, NuGet, VitePress, GitHub Actions, Aspire 13.1.2

**Dependency:** Requires completion of `2026-03-07-core-agent-migration-plan.md` (Phase A-H)

---

## Section 1: API Surface Review

### Task 1: Audit V3 public API surface

**Files:**
- Review: all files in `src/Core/V3/`

**Step 1: List all public types**

Run: `grep -rn "public" src/Core/V3/ --include="*.cs" | grep -E "(class|interface|record|enum|struct)" | sort`

**Step 2: Categorize each type**

Create a checklist:
- [ ] Is this type intended for public consumption?
- [ ] Is the naming self-explanatory?
- [ ] Are all `[Id(n)]` attributes sequential and stable?
- [ ] Are `[GenerateSerializer]` attributes on all Orleans-transmitted types?

**Step 3: Document the public API inventory**

Create `docs/api-surface.md` listing every public type, its purpose, and stability level.

**Step 4: Commit**

```bash
git add docs/api-surface.md
git commit -m "docs: create public API surface inventory"
```

### Task 2: Review namespace structure

**Files:**
- All files in `src/Core/V3/`

**Step 1: Verify namespace consistency**

All V3 files should use `Core.V3` or `Core.V3.*` namespaces. When graduating to release, this becomes `IAW.Core`:

| Current | Release |
|---------|---------|
| `Core.V3` | `IAW.Core` |
| `Core.V3.Messages` | `IAW.Core.Messages` |
| `Core.V3.Communication` | `IAW.Core.Communication` |
| `Core.V3.Context` | `IAW.Core.Context` |
| `Core.V3.Diagnostics` | `IAW.Core.Diagnostics` |
| `Core.V3.Observability` | `IAW.Core.Observability` |
| `Core.V3.Tools` | `IAW.Core.Tools` |
| `Core.V3.Registry` | `IAW.Core.Registry` |
| `Core.V3.Attributes` | `IAW.Core.Attributes` |
| `Core.V3.Samples` | `IAW.Core.Samples` |

**Step 2: Rename namespaces for release**

Use find-and-replace across all V3 files: `Core.V3` → `IAW.Core`

**Step 3: Build to verify**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add src/Core/
git commit -m "refactor: rename namespaces Core.V3 → IAW.Core for opensource release"
```

### Task 3: Review serialization IDs for stability

**Files:**
- All `[GenerateSerializer]` types in `src/Core/V3/`

**Step 1: List all serializable types and their ID assignments**

Run: `grep -A 20 "\[GenerateSerializer\]" src/Core/V3/ -r --include="*.cs"`

**Step 2: Verify ID sequential numbering**

Check each record:
- IDs start at 0
- IDs are sequential (no gaps)
- No duplicate IDs within a type
- No ID collisions between similar types

**Step 3: Document serialization contract**

Create `docs/serialization-contracts.md` listing all serializable types and their stable ID mappings.

**Step 4: Commit**

```bash
git add docs/serialization-contracts.md
git commit -m "docs: document serialization contracts for backward compatibility"
```

### Task 4: Review Orleans grain type IDs

**Files:**
- All grain classes in `src/Core/V3/`

**Step 1: List all GrainType attributes**

Expected:
- `Agent`: `[GrainType("agent-v3")]`
- `DynamicAgent`: `[GrainType("dynamic-agent-v3")]`
- `AgentRegistryGrain`: needs `[GrainType("agent-registry")]`

**Step 2: Verify grain type uniqueness**

No duplicates across the codebase.

**Step 3: Add missing GrainType attributes**

**Step 4: Commit**

```bash
git add src/Core/V3/
git commit -m "fix: ensure all grains have stable GrainType attributes"
```

### Task 5: Review IAgent interface for breaking changes

**Files:**
- `src/Core/V3/IAgent.cs`

**Step 1: Compare V3 IAgent with V2 IAgentV2**

Create mapping table:
| V2 Method | V3 Equivalent | Status |
|-----------|--------------|--------|
| GetProfileAsync | GetMetadataAsync | Renamed |
| RespondAsync | GetResponse | Simplified |
| AppendMessageAsync | (via GetResponse) | Removed |
| QueryMessagesAsync | GetHistory | Renamed |
| SetMemoryAsync | (via state) | Removed |
| GetMemoryAsync | GetStateAsync | Renamed |
| AppendEventAsync | HandleEventAsync | Renamed |
| QueryEventsAsync | GetEventLogAsync | Renamed |
| SubscribeAsync | (via IStreamConsumer<T>) | Moved |
| NotifyAsync | (via INotifier<T>) | Moved |
| StartScheduleAsync | StartTrackingAsync | Renamed |
| PublishStreamAsync | PublishToStreamAsync | Renamed |
| InvokeToolAsync | (via DefineTools) | Removed |

**Step 2: Document migration guide**

Create `docs/migration-v2-to-v3.md` with the mapping table and code examples.

**Step 3: Commit**

```bash
git add docs/migration-v2-to-v3.md
git commit -m "docs: create V2 → V3 migration guide"
```

### Task 6: Check for accidental internal type exposure

**Files:**
- All V3 files

**Step 1: Find types that should be internal**

Types that should NOT be public:
- `DurableChatHistoryProvider` — internal implementation detail
- `BuildSafeErrorMessage` — internal helper
- `EventTypeToStreamName` — could be public utility, verify

**Step 2: Mark internal types appropriately**

Change `public` to `internal` where needed. Keep `EventTypeToStreamName` public (useful for consumers).

**Step 3: Build and test**

Run: `dotnet build src/Core/Core.csproj && dotnet test IAW.slnx`

**Step 4: Commit**

```bash
git add src/Core/V3/
git commit -m "refactor: mark internal implementation types as internal"
```

---

## Section 2: Documentation

### Task 7: Create Getting Started guide

**Files:**
- Create: `website/guide/index.md`

**Step 1: Write Getting Started page**

Content outline:
1. What is IAW — 2 sentences
2. Install — `dotnet add package IAW.Core`
3. Create your first agent — minimal code sample
4. Run with Aspire — 3 lines
5. Talk to your agent — HTTP endpoint
6. Next steps — links to concepts

**Step 2: Include complete code sample**

```csharp
public interface IGreeterAgent : IAgent;

public class GreeterAgent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("v3-history")] IDurableList<ChatMessage> history,
    [Memory("v3-tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
    : Agent(state, eventLog, chatClient, history, trackingItems), IGreeterAgent
{
    protected override string Instructions => "You are a friendly greeter.";
    protected override string DisplayName => "Greeter";
}
```

**Step 3: Commit**

```bash
git add website/guide/index.md
git commit -m "docs: add Getting Started guide"
```

### Task 8: Create Architecture overview page

**Files:**
- Create: `website/guide/architecture.md`

**Step 1: Write architecture page**

Sections:
1. Agent as Orleans Grain — DurableGrain, journaled state
2. Behavior Composition — interfaces, not inheritance
3. Typed Message System — ICommand, IEvent, INotification
4. Stream Patterns — pipeline, fan-out, fan-in
5. AI Integration — Microsoft.Agents.AI, IChatClient
6. Tools — built-in + custom via DefineTools()
7. Registry — auto-discovery at startup

Include diagrams as Mermaid.

**Step 2: Commit**

```bash
git add website/guide/architecture.md
git commit -m "docs: add Architecture overview page"
```

### Task 9: Create Building Agents guide

**Files:**
- Create: `website/guide/building-agents.md`

**Step 1: Write agent creation guide**

Sections:
1. The Agent base class
2. Constructor parameters (what each Memory does)
3. Overriding Instructions and DisplayName
4. Adding custom tools via DefineTools()
5. Using context providers
6. Handling errors
7. Testing your agent

Full code examples for each section.

**Step 2: Commit**

```bash
git add website/guide/building-agents.md
git commit -m "docs: add Building Agents guide"
```

### Task 10: Create Behaviors guide — Conversation

**Files:**
- Create: `website/guide/behaviors/conversation.md`

**Step 1: Write conversation behavior guide**

Sections:
1. GetResponse — simple request/response
2. GetResponseStream — streaming with IAsyncEnumerable
3. Chat history — GetHistory, ClearHistoryAsync
4. Context providers — injecting RAG/knowledge
5. Token tracking — usage metadata
6. Error handling — AgentResponseKind.Error

**Step 2: Commit**

```bash
git add website/guide/behaviors/
git commit -m "docs: add Conversation behavior guide"
```

### Task 11: Create Behaviors guide — Events & Streams

**Files:**
- Create: `website/guide/behaviors/events-streams.md`

**Step 1: Write events and streams guide**

Sections:
1. Event publishing — PublishAsync, typed events
2. Event handling — HandleEvent override
3. Event log — GetEventLogAsync
4. Stream consumption — IStreamConsumer<T>
5. Stream production — IStreamProducer<T>
6. Auto-subscription — how OnActivateAsync wires streams
7. Stream name resolution — type → "dot.case"
8. Patterns — pipeline, fan-out, fan-in (with diagrams)

**Step 2: Commit**

```bash
git add website/guide/behaviors/events-streams.md
git commit -m "docs: add Events & Streams behavior guide"
```

### Task 12: Create Behaviors guide — Tracking

**Files:**
- Create: `website/guide/behaviors/tracking.md`

**Step 1: Write tracking behavior guide**

Sections:
1. What tracking is — recurring autonomous checks
2. Starting tracking — StartTrackingAsync
3. The tracking callback — OnTrackingDueAsync
4. Change detection — "tracking.changed" event
5. Built-in tools — StartTracking, StopTracking, ListTracking
6. DurableJobs vs Reminders — when to use which

**Step 2: Commit**

```bash
git add website/guide/behaviors/tracking.md
git commit -m "docs: add Tracking behavior guide"
```

### Task 13: Create Behaviors guide — Tools

**Files:**
- Create: `website/guide/behaviors/tools.md`

**Step 1: Write tools behavior guide**

Sections:
1. Built-in tools — Workspace, File, Shell, Web
2. Custom tools — DefineTools() with AIFunctionFactory
3. Tool descriptions — [Description] attribute
4. Tool parameters — named, typed, with descriptions
5. Security — workspace validation, output truncation

**Step 2: Commit**

```bash
git add website/guide/behaviors/tools.md
git commit -m "docs: add Tools behavior guide"
```

### Task 14: Create Message Types guide

**Files:**
- Create: `website/guide/messages.md`

**Step 1: Write message types guide**

Sections:
1. IAgentMessage — base marker
2. ICommand — directed, point-to-point
3. IEvent — broadcast, informational
4. INotification — targeted, advisory
5. Creating custom message types
6. Serialization requirements — [GenerateSerializer], [Id(n)]
7. Stream name resolution
8. Built-in message types — table of all

**Step 2: Commit**

```bash
git add website/guide/messages.md
git commit -m "docs: add Message Types guide"
```

### Task 15: Create Use Cases walkthrough

**Files:**
- Create: `website/tutorials/use-cases/code-review-bot.md`
- Create: `website/tutorials/use-cases/infra-monitor.md`
- Create: `website/tutorials/use-cases/personal-assistant.md`
- Create: `website/tutorials/use-cases/knowledge-base.md`
- Create: `website/tutorials/use-cases/cicd-pipeline.md`

**Step 1: Write UC1 — Code Review Bot**

Complete walkthrough:
1. Agent definition with IStreamConsumer<CodeChangedEvent>
2. How stream auto-subscription works
3. Processing code changes
4. Publishing review notifications
5. Testing the agent
6. Registering in Aspire

**Step 2: Write UC2 — Infrastructure Monitor**

Complete walkthrough:
1. Agent with tracking behavior
2. Setting up health check intervals
3. Publishing HealthCheckEvent
4. Alert notifications on degradation
5. Dashboard integration

**Step 3: Write UC3 — Personal Assistant**

Complete walkthrough:
1. Task decomposition
2. Broadcasting AssignTaskCommand
3. Collecting ProgressNotification
4. Conversation interface

**Step 4: Write UC4 — Knowledge Base**

Complete walkthrough:
1. Minimal agent (conversation + tools only)
2. Custom tools for document search
3. Context providers for RAG
4. No streams/events needed

**Step 5: Write UC5 — CI/CD Pipeline**

Complete walkthrough:
1. Pipeline as event chain
2. IStreamConsumer<CodeChangedEvent> + IStreamProducer<BuildCompletedEvent>
3. Typed pipeline — no orchestrator
4. Fan-out to multiple build targets
5. Alert on failure

**Step 6: Commit**

```bash
git add website/tutorials/use-cases/
git commit -m "docs: add 5 use case walkthroughs — code review, infra, assistant, KB, CI/CD"
```

### Task 16: Create Testing guide

**Files:**
- Create: `website/guide/testing.md`

**Step 1: Write testing guide**

Sections:
1. AgentTest<T> — universal behavior tests (inherit one line, get 16 tests)
2. Writing custom tests
3. MockChatClient — simulating LLM responses
4. ScenarioBuilder — fluent Given/When/Then
5. AspireAgentTest<T> — full integration tests
6. Testing streams — verifying event delivery
7. Testing tools — mock workspace
8. Test isolation — unique IDs per run

**Step 2: Commit**

```bash
git add website/guide/testing.md
git commit -m "docs: add Testing guide"
```

### Task 17: Create API Reference index

**Files:**
- Create: `website/reference/index.md`

**Step 1: Write API reference**

Organized by namespace:
- IAW.Core — Agent, IAgent, models
- IAW.Core.Messages — IAgentMessage, ICommand, IEvent, INotification, built-in types
- IAW.Core.Communication — IStreamConsumer<T>, IStreamProducer<T>, IBroadcaster<T>, INotifier<T>, IReceiver<T>
- IAW.Core.Context — IAIContextProvider, AIContext
- IAW.Core.Tools — WorkspaceTools, FileTools, ShellTools, WebTools
- IAW.Core.Diagnostics — ISelfDiagnosable, DiagnosticReport
- IAW.Core.Observability — AgentTelemetry
- IAW.Core.Attributes — CapabilityAttribute, PublishesAttribute, SubscribesAttribute
- IAW.Core.Registry — IAgentRegistryGrain, AgentRegistration

**Step 2: Commit**

```bash
git add website/reference/
git commit -m "docs: add API Reference index"
```

### Task 18: Update VitePress navigation config

**Files:**
- Modify: `website/.vitepress/config.mts`

**Step 1: Read current config**

Read file and understand current sidebar structure.

**Step 2: Update sidebar with new pages**

```typescript
sidebar: {
  '/guide/': [
    {
      text: 'Introduction',
      items: [
        { text: 'Getting Started', link: '/guide/' },
        { text: 'Architecture', link: '/guide/architecture' },
      ]
    },
    {
      text: 'Core Concepts',
      items: [
        { text: 'Building Agents', link: '/guide/building-agents' },
        { text: 'Message Types', link: '/guide/messages' },
      ]
    },
    {
      text: 'Behaviors',
      items: [
        { text: 'Conversation', link: '/guide/behaviors/conversation' },
        { text: 'Events & Streams', link: '/guide/behaviors/events-streams' },
        { text: 'Tracking', link: '/guide/behaviors/tracking' },
        { text: 'Tools', link: '/guide/behaviors/tools' },
      ]
    },
    {
      text: 'Integrations',
      items: [
        { text: 'Testing', link: '/guide/testing' },
        { text: 'MCP Server', link: '/guide/mcp-server' },
        { text: 'Telegram Bot', link: '/guide/telegram-bot' },
      ]
    }
  ],
  '/tutorials/': [
    {
      text: 'Use Cases',
      items: [
        { text: 'Code Review Bot', link: '/tutorials/use-cases/code-review-bot' },
        { text: 'Infrastructure Monitor', link: '/tutorials/use-cases/infra-monitor' },
        { text: 'Personal Assistant', link: '/tutorials/use-cases/personal-assistant' },
        { text: 'Knowledge Base', link: '/tutorials/use-cases/knowledge-base' },
        { text: 'CI/CD Pipeline', link: '/tutorials/use-cases/cicd-pipeline' },
      ]
    }
  ],
  '/reference/': [
    {
      text: 'API Reference',
      items: [
        { text: 'Overview', link: '/reference/' },
      ]
    }
  ]
}
```

**Step 3: Build website to verify**

Run: `cd website && npm run build`

**Step 4: Commit**

```bash
git add website/.vitepress/config.mts
git commit -m "docs: update VitePress navigation with new behavior guides and use cases"
```

### Task 19: Update homepage with V3 features

**Files:**
- Modify: `website/index.md`

**Step 1: Read current homepage**

**Step 2: Update hero section and features**

Update tagline, features grid, and quick start to reflect V3:
- Typed message system
- Stream-based event pipelines
- Compose behaviors via interfaces
- Built-in tools (File, Shell, Web)
- Update code sample to V3 Agent

**Step 3: Commit**

```bash
git add website/index.md
git commit -m "docs: update homepage for V3 launch"
```

---

## Section 3: CI/CD & Packaging

### Task 20: Create GitHub Actions CI workflow

**Files:**
- Create: `.github/workflows/ci.yml`

**Step 1: Create CI workflow**

```yaml
name: CI
on:
  push:
    branches: [main, master]
  pull_request:
    branches: [main, master]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '11.0.x'
      - run: dotnet build IAW.slnx
      - run: dotnet test IAW.slnx --logger "trx;LogFileName=results.trx"
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: '**/TestResults/*.trx'
```

**Step 2: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add GitHub Actions CI workflow — build + test"
```

### Task 21: Create NuGet package workflow

**Files:**
- Create: `.github/workflows/nuget.yml`

**Step 1: Create NuGet publish workflow**

```yaml
name: Publish NuGet
on:
  push:
    tags: ['v*']

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '11.0.x'
      - run: dotnet build IAW.slnx -c Release
      - run: dotnet test IAW.slnx -c Release
      - run: dotnet pack src/Core/Core.csproj -c Release -o ./nupkgs
      - run: dotnet nuget push ./nupkgs/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }}
```

**Step 2: Commit**

```bash
git add .github/workflows/nuget.yml
git commit -m "ci: add NuGet publish workflow on tag push"
```

### Task 22: Configure NuGet package metadata

**Files:**
- Modify: `src/Core/Core.csproj`

**Step 1: Add NuGet package metadata**

```xml
<PropertyGroup>
    <PackageId>IAW.Core</PackageId>
    <Version>3.0.0-preview.1</Version>
    <Authors>IAW Contributors</Authors>
    <Description>Orleans-based multi-agent runtime with typed message system, streaming behaviors, and AI integration</Description>
    <PackageTags>orleans;agents;ai;distributed;streaming</PackageTags>
    <RepositoryUrl>https://github.com/user/IAW</RepositoryUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <IsPackable>true</IsPackable>
</PropertyGroup>
```

**Step 2: Build and create local pack to verify**

Run: `dotnet pack src/Core/Core.csproj -c Release -o ./nupkgs`

**Step 3: Commit**

```bash
git add src/Core/Core.csproj
git commit -m "ci: add NuGet package metadata to Core.csproj"
```

---

## Section 4: Licensing & Legal

### Task 23: Add MIT license

**Files:**
- Create: `LICENSE`

**Step 1: Create LICENSE file**

```
MIT License

Copyright (c) 2026 IAW Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

**Step 2: Commit**

```bash
git add LICENSE
git commit -m "legal: add MIT license"
```

### Task 24: Add CONTRIBUTING.md

**Files:**
- Create: `CONTRIBUTING.md`

**Step 1: Write contribution guide**

Sections:
1. How to contribute
2. Development setup
3. Running tests
4. Code style (no XML comments, self-explanatory naming)
5. PR process
6. Issue templates

**Step 2: Commit**

```bash
git add CONTRIBUTING.md
git commit -m "docs: add CONTRIBUTING.md"
```

### Task 25: Add CODE_OF_CONDUCT.md

**Files:**
- Create: `CODE_OF_CONDUCT.md`

Standard Contributor Covenant.

**Step 1: Create and commit**

```bash
git add CODE_OF_CONDUCT.md
git commit -m "docs: add Code of Conduct"
```

---

## Section 5: Security Audit

### Task 26: Audit tool security — FileTools

**Files:**
- Review: `src/Core/V3/Tools/FileTools.cs`

**Step 1: Verify path traversal protection**

Check `ValidateInsideWorkspace` prevents:
- `../../etc/passwd`
- Symlink attacks
- Null byte injection

**Step 2: Verify output size limits**

Check `MaxResults` caps prevent DoS.

**Step 3: Document findings**

### Task 27: Audit tool security — ShellTools

**Files:**
- Review: `src/Core/V3/Tools/ShellTools.cs`

**Step 1: Check command injection vectors**

Verify shell command construction:
- Windows: `cmd.exe /c {command}` — verify no injection via `{command}`
- Linux: `/bin/sh -c "{command}"` — verify escaping

**Step 2: Check timeout enforcement**

Verify 120s timeout kills runaway processes.

**Step 3: Document findings and add mitigations if needed**

### Task 28: Audit tool security — WebTools

**Files:**
- Review: `src/Core/V3/Tools/WebTools.cs`

**Step 1: Check SSRF protection**

Verify no internal network access (localhost, 10.x, 192.168.x).

**Step 2: Check response size limits**

Verify 50KB truncation.

**Step 3: Add SSRF protection if missing**

```csharp
private static readonly HashSet<string> BlockedHosts = new(StringComparer.OrdinalIgnoreCase)
{
    "localhost", "127.0.0.1", "0.0.0.0", "::1"
};
```

### Task 29: Audit Orleans grain security

**Files:**
- Review: all grain interfaces

**Step 1: Check grain ID predictability**

Verify agents use unique, non-guessable IDs in production.

**Step 2: Check state isolation**

Verify one grain cannot access another's durable state.

**Step 3: Document security model in docs**

Create `docs/security.md` with security model documentation.

### Task 30: Audit serialization security

**Step 1: Check for deserialization vulnerabilities**

Verify `[GenerateSerializer]` types don't deserialize arbitrary types.

**Step 2: Check `Dictionary<string, object>` usage**

The legacy `AgentEvent.Payload` uses `Dictionary<string, object>` which could carry arbitrary types. Verify Orleans source generator handles this safely.

**Step 3: Document known limitations**

---

## Section 6: README & Repository Polish

### Task 31: Create comprehensive README.md

**Files:**
- Create: `README.md` (if not exists, or update)

**Step 1: Write README**

Sections:
1. Logo + title
2. One-line description
3. Badges (CI, NuGet, License)
4. Features — bullet points with links
5. Quick Start — 5-minute setup
6. Architecture diagram (Mermaid)
7. Use Cases — table with links
8. Documentation — link to VitePress site
9. Contributing — link to CONTRIBUTING.md
10. License — MIT

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add comprehensive README for opensource launch"
```

### Task 32: Create .editorconfig

**Files:**
- Create: `.editorconfig`

**Step 1: Create .editorconfig matching code style**

```ini
root = true

[*.cs]
indent_style = space
indent_size = 4
charset = utf-8
end_of_line = lf
insert_final_newline = true
trim_trailing_whitespace = true

# No XML doc comments
dotnet_diagnostic.CS1591.severity = none

# Expression body preferences
csharp_style_expression_bodied_methods = when_on_single_line
csharp_style_expression_bodied_properties = true
csharp_style_expression_bodied_constructors = false

# Primary constructors
csharp_style_prefer_primary_constructors = true

# File-scoped namespaces
csharp_style_namespace_declarations = file_scoped
```

**Step 2: Commit**

```bash
git add .editorconfig
git commit -m "style: add .editorconfig for code style enforcement"
```

### Task 33: Create .gitignore review

**Files:**
- Review: `.gitignore`

**Step 1: Verify .gitignore covers**

- bin/, obj/
- .vs/, .idea/
- *.user, *.suo
- TestResults/
- nupkgs/
- node_modules/
- .env

**Step 2: Add missing entries if needed**

### Task 34: Add issue templates

**Files:**
- Create: `.github/ISSUE_TEMPLATE/bug_report.md`
- Create: `.github/ISSUE_TEMPLATE/feature_request.md`

**Step 1: Create bug report template**

**Step 2: Create feature request template**

**Step 3: Commit**

```bash
git add .github/ISSUE_TEMPLATE/
git commit -m "docs: add GitHub issue templates"
```

### Task 35: Add PR template

**Files:**
- Create: `.github/PULL_REQUEST_TEMPLATE.md`

**Step 1: Create PR template with checklist**

```markdown
## Summary
<!-- What does this PR do? -->

## Checklist
- [ ] Tests pass (`dotnet test IAW.slnx`)
- [ ] No breaking API changes (or documented in migration guide)
- [ ] Documentation updated if needed
- [ ] Self-explanatory naming (no XML doc comments needed)
```

**Step 2: Commit**

```bash
git add .github/PULL_REQUEST_TEMPLATE.md
git commit -m "docs: add PR template"
```

---

## Section 7: Sample Projects

### Task 36: Create standalone Getting Started sample

**Files:**
- Create: `samples/GettingStarted/GettingStarted.csproj`
- Create: `samples/GettingStarted/Program.cs`
- Create: `samples/GettingStarted/GreeterAgent.cs`

**Step 1: Create minimal project that readers can clone and run**

Standalone Aspire project with one agent, one endpoint.

**Step 2: Verify it runs**

Run: `dotnet run --project samples/GettingStarted/GettingStarted.csproj`

**Step 3: Commit**

```bash
git add samples/GettingStarted/
git commit -m "samples: add Getting Started standalone sample"
```

### Task 37: Create Stream Pipeline sample

**Files:**
- Create: `samples/StreamPipeline/`

**Step 1: Create sample showing CodeChanged → Build → Test → Deploy pipeline**

Three agents, typed events, auto-wired streams. Zero orchestration code.

**Step 2: Verify it runs**

**Step 3: Commit**

```bash
git add samples/StreamPipeline/
git commit -m "samples: add Stream Pipeline sample — typed event chain"
```

### Task 38: Create DynamicAgent sample

**Files:**
- Create: `samples/DynamicAgents/`

**Step 1: Create sample showing runtime agent configuration**

Configure via HTTP endpoint, show runtime tool addition.

**Step 2: Verify it runs**

**Step 3: Commit**

```bash
git add samples/DynamicAgents/
git commit -m "samples: add DynamicAgent sample — runtime configuration"
```

---

## Section 8: Final Pre-Launch Checks

### Task 39: Full solution build and test

**Step 1:** `dotnet build IAW.slnx`
**Step 2:** `dotnet test IAW.slnx`
**Step 3:** Fix any failures

### Task 40: Aspire smoke test

**Step 1:** `aspire run --project src/IAW.AppHost/Aspire.csproj`
**Step 2:** Verify dashboard shows all agents
**Step 3:** Test sample endpoints

### Task 41: Website build and preview

**Step 1:** `cd website && npm install && npm run build`
**Step 2:** `npm run preview` — verify all pages render
**Step 3:** Check for broken links

### Task 42: NuGet pack verification

**Step 1:** `dotnet pack src/Core/Core.csproj -c Release -o ./nupkgs`
**Step 2:** Inspect package contents
**Step 3:** Verify package metadata

### Task 43: Create release checklist

**Files:**
- Create: `docs/release-checklist.md`

Document:
1. All tests pass
2. Documentation built
3. NuGet package created
4. CHANGELOG updated
5. Git tag created
6. GitHub release created
7. NuGet published
8. Website deployed
9. Announcement posted

---

## Summary: Opensource Readiness Task Count

| Section | Tasks | Steps |
|---------|-------|-------|
| API Surface Review | 6 | ~50 |
| Documentation | 13 | ~180 |
| CI/CD & Packaging | 3 | ~25 |
| Licensing & Legal | 3 | ~10 |
| Security Audit | 5 | ~40 |
| README & Repo Polish | 5 | ~30 |
| Sample Projects | 3 | ~30 |
| Final Checks | 5 | ~25 |
| **Total** | **43** | **~390 steps** |

Note: Each step expands to 3-5 micro-actions bringing effective count to ~700.
