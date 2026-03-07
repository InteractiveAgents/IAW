# V3 Promotion & Public Launch Design

**Date:** 2026-03-07
**Status:** Approved
**Version:** 0.0.1

## Goal

Promote V3 to main, delete V1/V2, port all 18 production agents from `E:\IAW\src\Agents\`, rename namespaces to `IAW.Core`, and ship as v0.0.1 public NuGet packages.

## Decisions

| Decision | Choice |
|----------|--------|
| Approach | Big Bang — single branch, one coordinated migration |
| Agents | All 18 production agents ship out-of-the-box |
| Packaging | Two agent packages: IAW.Agents (14) + IAW.Agents.CSharp (4) |
| V1/V2 | Delete completely, no deprecation period |
| Constructor | Keep explicit [Memory] params (V3 style) |
| Namespace | `Core.V3.*` → `IAW.Core.*` |
| Version | 0.0.1 (early experimental) |
| Memory keys | No version prefixes — `[Memory("history")]` not `[Memory("v3-history")]` |

## Architecture

### Namespace Migration

| Current | Target |
|---------|--------|
| `Core.V3` | `IAW.Core` |
| `Core.V3.Communication` | `IAW.Core.Communication` |
| `Core.V3.Messages` | `IAW.Core.Messages` |
| `Core.V3.Registry` | `IAW.Core.Registry` |
| `Core.V3.Observability` | `IAW.Core.Observability` |
| `Core.V3.Diagnostics` | `IAW.Core.Diagnostics` |
| `Core.V3.Context` | `IAW.Core.Context` |
| `Core.V3.Tools` | `IAW.Core.Tools` |
| `Core.V3.Attributes` | `IAW.Core.Attributes` |

### V1/V2 Deletion

**Delete:**
- `src/Core/Agent.cs` (V1 shim, 304 lines)
- `src/Core/IAgent.cs`, `src/Core/IAgentBehaviors.cs`
- `src/Core/AgentContracts.cs`, `src/Core/NotificationJson.cs`
- `src/Core/Observability.cs` (V1 telemetry)
- `src/Core/V2/` entire directory (AgentV2, IAgentV2, all V2 contracts)
- All V1/V2 test files

**Keep** (shared infrastructure):
- `src/Core/AI/` (LLM models, LlmAttribute, registration)
- `src/Core/GitHub/` (GitHub service)
- `src/Core/Routing/` (IAgentRouter)
- `src/Core/MemoryAttribute.cs`
- `src/Core/TrackingOptions.cs`
- `src/Core/IMonitorSourceProvider.cs`

### Agent Constructor Alignment

Production V1 (4 params):
```csharp
Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateDescriptor> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    [Memory("tracking-items")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Llm<Model>] IChatClient chatClient)
```

V3 target (5 params):
```csharp
Agent(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems)
```

Key changes per agent:
1. Add `[Memory("history")] IDurableList<ChatMessage> history` parameter
2. `StateDescriptor` → `StateEntry`
3. `SystemPrompt` → `Instructions`
4. Drop V1 marker interfaces (IConversationalAgent, IStatefulAgent, etc.)
5. Keep communication interfaces (IReceiver<T>, IStreamConsumer<T>)
6. Add `[GrainType("agent-name")]` attribute
7. `[Llm<T>]` attribute stays for LLM injection

### Package Structure

| Package | Contents |
|---------|----------|
| **IAW.Core** | Agent, IAgent, DynamicAgent, Communication, Messages, Registry, Tools, Observability, Diagnostics, AI/LLM, Attributes |
| **IAW.Agents** | 14 agents: FileSystem, Shell, Git, Build, Aspire, PersonalAssistant, Planning, Notification, Deployer, Reviewer, SelfImprovement, Knowledge, User + grain interfaces + messages |
| **IAW.Agents.CSharp** | 4 agents: Roslyn, DotNet, NuGet, GitHub + RoslynTools |
| **IAW.Testing** | AgentTest<T> (renamed from AgentTestV3<T>), MockChatClient |
| **IAW.Hosting** | Aspire extensions |
| **IAW.MCP** | MCP server bridge |

### Project Layout

```
src/
  Core/Core.csproj                    IAW.Core
    AI/                               LLM models, registration
    Attributes/                       Capability, Publishes, Subscribes
    Communication/                    IStreamConsumer, IBroadcaster, IReceiver, etc.
    Context/                          AIContext, IAIContextProvider
    Diagnostics/                      ISelfDiagnosable, DiagnosticReport
    GitHub/                           GitHub service
    Messages/                         11 built-in message types
    Observability/                    AgentTelemetry
    Registry/                         AgentRegistryGrain, AgentRegistration
    Routing/                          IAgentRouter
    Tools/                            FileTools, ShellTools, WebTools, WorkspaceTools
    Agent.cs                          Main abstract partial class
    Agent.Events.cs                   Event handling
    Agent.Lifecycle.cs                Metadata, capabilities, diagnostics
    Agent.Observers.cs                Observer support
    Agent.State.cs                    State management
    Agent.Streams.cs                  Stream pub/sub
    Agent.Tools.cs                    Tool registration
    Agent.Tracking.cs                 Reminders/tracking
    DynamicAgent.cs                   Runtime-configurable agent
    IAgent.cs                         13-method interface
    ...contract types...
  Agents/Agents.csproj                IAW.Agents
    Infrastructure/                   FileSystem, Shell, Git, Build, Aspire + interfaces
    Orchestration/                    PersonalAssistant, Planning, Notification, Deployer + interfaces
    Review/                           Reviewer, SelfImprovement + interfaces
    Knowledge/                        Knowledge, User + interfaces
    Messages/                         Agent-specific message types
  Agents.CSharp/Agents.CSharp.csproj  IAW.Agents.CSharp
    RoslynAgent.cs, DotNetAgent.cs, NuGetAgent.cs, GitHubAgent.cs
    Tools/RoslynTools.cs
  IAW.Testing/                        IAW.Testing
  IAW.AppHost/                        Aspire host
  ...
samples/
  Samples/                            Demo agents (CodeReview, CIPipeline, InfraMonitor, etc.)
test/
  Core.Tests/                         All tests
```

### 18 Production Agents

**Infrastructure (5):**
| Agent | Grain Key | Interface | Model |
|-------|-----------|-----------|-------|
| FileSystemAgent | file-system | IFileSystem | Claude45Haiku |
| ShellAgent | shell | IShell | Claude45Haiku |
| GitAgent | git | IGit | Claude45Haiku |
| BuildAgent | build | IBuild | Claude45Haiku |
| AspireAgent | aspire | IAspire | Claude45Haiku |

**Orchestration (4):**
| Agent | Grain Key | Interface | Model |
|-------|-----------|-----------|-------|
| PersonalAssistantAgent | personal-assistant | IPersonalAssistant | Sonnet46 |
| PlanningAgent | planning | IPlanning | Claude45Haiku |
| NotificationAgent | notification | INotification | Claude45Haiku |
| DeployerAgent | deployer | IDeployer | Claude45Haiku |

**Review (2):**
| Agent | Grain Key | Interface | Model |
|-------|-----------|-----------|-------|
| ReviewerAgent | reviewer | IReviewer | Sonnet46 |
| SelfImprovementAgent | self-improvement | ISelfImprovement | Sonnet46 |

**Knowledge (2):**
| Agent | Grain Key | Interface | Model |
|-------|-----------|-----------|-------|
| KnowledgeAgent | knowledge | IKnowledge | Sonnet46 |
| UserAgent | user | IUser | Claude45Haiku |

**CSharp (4):**
| Agent | Grain Key | Interface | Model |
|-------|-----------|-----------|-------|
| RoslynAgent | roslyn | IRoslyn | Claude45Haiku |
| DotNetAgent | dot-net | IDotNet | Claude45Haiku |
| NuGetAgent | nu-get | INuGet | Claude45Haiku |
| GitHubAgent | git-hub | IGitHub | Claude45Haiku |

### Sample Agents (moved to samples/)

- CodeReviewAgent, CIPipelineAgent, InfraMonitorAgent, PersonalAssistantAgent (demo), KnowledgeBaseAgent, WeatherAgent

### Testing

- `AgentTestV3<T>` → `AgentTest<T>` (no version suffix)
- Each ported agent gets: `class FileSystemAgentTests : AgentTest<FileSystemAgent>`
- 144 existing tests preserved + ~18 new agent test classes

### Documentation & Cleanup

- Update CLAUDE.md (remove V1/V2 references)
- Update README.md for public audience
- Update all docs/ pages with IAW.Core namespace
- Remove migration-v2-to-v3.md (internal history)
- NuGet metadata: version 0.0.1, MIT license, repo URL
