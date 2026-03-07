# V3 Promotion & Public Launch Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Promote V3 to main namespace (IAW.Core), delete V1/V2, port all 18 production agents, and ship as v0.0.1.

**Architecture:** Big Bang migration in a single branch. The production IAW system at `E:\IAW\src\` is the source of truth for agent implementations. V3 at `src/Core/V3/` provides the target architecture. We merge the best of both: production agent code (business logic) + V3 structure (namespace, constructor pattern, partial classes).

**Tech Stack:** .NET 11, Orleans 10.0, Microsoft.Extensions.AI, Microsoft.Agents.AI, xunit.v3, Aspire 13.1

---

## Phase 1: Delete V1/V2 Code

### Task 1: Delete V1 Agent Shim and Contracts

**Files:**
- Delete: `src/Core/Agent.cs` (V1 shim, 304 lines)
- Delete: `src/Core/IAgent.cs` (V1 interface extending IAgentV2 + 8 behavior interfaces)
- Delete: `src/Core/IAgentBehaviors.cs` (8 V1 behavior interfaces)
- Delete: `src/Core/AgentContracts.cs` (AgentMetadata, AgentHistoryEntry, AgentEventRecord, NotificationEnvelope, NotificationRecord, AgentTrackingStatus)
- Delete: `src/Core/NotificationJson.cs`
- Delete: `src/Core/Observability.cs` (V1 AgentObservability)

**Step 1: Delete the V1 files**

```bash
cd "E:/IAW/InteractiveAgents/IAW"
rm src/Core/Agent.cs src/Core/IAgent.cs src/Core/IAgentBehaviors.cs src/Core/AgentContracts.cs src/Core/NotificationJson.cs src/Core/Observability.cs
```

**Step 2: Verify build still has V3 + V2**

Run: `dotnet build src/Core/Core.csproj 2>&1 | head -30`
Expected: Errors referencing `Core.Agent` or `Core.IAgent` from V2 code — this is fine, we delete V2 next.

**Step 3: Commit**

```bash
git add -A src/Core/Agent.cs src/Core/IAgent.cs src/Core/IAgentBehaviors.cs src/Core/AgentContracts.cs src/Core/NotificationJson.cs src/Core/Observability.cs
git commit -m "chore: delete V1 agent shim and contracts"
```

### Task 2: Delete V2 Code

**Files:**
- Delete: `src/Core/V2/AgentV2.cs`
- Delete: `src/Core/V2/IAgentV2.cs`
- Delete: `src/Core/V2/AgentEvent.cs`
- Delete: `src/Core/V2/AgentEventQuery.cs`
- Delete: `src/Core/V2/AgentMessage.cs`
- Delete: `src/Core/V2/AgentMessageQuery.cs`
- Delete: `src/Core/V2/AgentProfile.cs`
- Delete: `src/Core/V2/AgentReply.cs`
- Delete: `src/Core/V2/AgentRequest.cs`
- Delete: `src/Core/V2/ScheduleStatus.cs`

**Step 1: Delete the entire V2 directory**

```bash
rm -rf src/Core/V2
```

**Step 2: Commit**

```bash
git add -A src/Core/V2
git commit -m "chore: delete V2 agent code"
```

### Task 3: Delete V1/V2 Test Files

**Files:**
- Delete: `test/Core.Tests/CoreAgentTests.cs` (V1 AgentTest)
- Delete: `test/Core.Tests/CoreAgentV2Tests.cs` (V2 tests)
- Delete: `test/Core.Tests/TestAgentV2.cs` (V2 test grain)
- Delete: `test/Core.Tests/ScenarioBuilderTests.cs` (V2 scenario tests)
- Delete: `test/Core.Tests/ArchitectureGuardTests.cs` (V1 architecture guards)

**Step 1: Delete V1/V2 test files**

```bash
rm test/Core.Tests/CoreAgentTests.cs test/Core.Tests/CoreAgentV2Tests.cs test/Core.Tests/TestAgentV2.cs test/Core.Tests/ScenarioBuilderTests.cs test/Core.Tests/ArchitectureGuardTests.cs
```

**Step 2: Commit**

```bash
git add -A test/Core.Tests/
git commit -m "chore: delete V1/V2 test files"
```

---

## Phase 2: Rename V3 Namespace to IAW.Core

### Task 4: Rename Core V3 Namespaces

All files under `src/Core/V3/` get namespace `Core.V3` → `IAW.Core`. Sub-namespaces follow: `Core.V3.Communication` → `IAW.Core.Communication`, etc.

**Files to modify** (all files in `src/Core/V3/` — ~68 files):
- Every `.cs` file: change `namespace Core.V3` → `namespace IAW.Core`
- Every `.cs` file: change `using Core.V3` → `using IAW.Core`
- Change `Core.V3.Communication` → `IAW.Core.Communication`
- Change `Core.V3.Messages` → `IAW.Core.Messages`
- Change `Core.V3.Registry` → `IAW.Core.Registry`
- Change `Core.V3.Observability` → `IAW.Core.Observability`
- Change `Core.V3.Diagnostics` → `IAW.Core.Diagnostics`
- Change `Core.V3.Context` → `IAW.Core.Context`
- Change `Core.V3.Tools` → `IAW.Core.Tools`
- Change `Core.V3.Attributes` → `IAW.Core.Attributes`
- Change `Core.V3.Samples` → `IAW.Core.Samples` (temporarily, moved to samples/ later)

**Step 1: Run sed replacements across all V3 source files**

```bash
find src/Core/V3 -name "*.cs" -not -path "*/obj/*" -exec sed -i \
  -e 's/namespace Core\.V3/namespace IAW.Core/g' \
  -e 's/using Core\.V3/using IAW.Core/g' \
  -e 's/global::Core\.V3/global::IAW.Core/g' \
  {} +
```

**Step 2: Move V3 files up — flatten directory structure**

Move all V3 subdirectories to sit directly under `src/Core/`:

```bash
# Move V3 subdirectories up
mv src/Core/V3/Communication src/Core/Communication
mv src/Core/V3/Messages src/Core/Messages
mv src/Core/V3/Registry src/Core/Registry
mv src/Core/V3/Observability src/Core/Observability
mv src/Core/V3/Diagnostics src/Core/Diagnostics
mv src/Core/V3/Context src/Core/Context
mv src/Core/V3/Tools src/Core/Tools
mv src/Core/V3/Attributes src/Core/Attributes
mv src/Core/V3/Samples src/Core/Samples

# Move V3 root files up
mv src/Core/V3/Agent.cs src/Core/Agent.cs
mv src/Core/V3/Agent.Events.cs src/Core/Agent.Events.cs
mv src/Core/V3/Agent.Lifecycle.cs src/Core/Agent.Lifecycle.cs
mv src/Core/V3/Agent.Observers.cs src/Core/Agent.Observers.cs
mv src/Core/V3/Agent.State.cs src/Core/Agent.State.cs
mv src/Core/V3/Agent.Streams.cs src/Core/Agent.Streams.cs
mv src/Core/V3/Agent.Tools.cs src/Core/Agent.Tools.cs
mv src/Core/V3/Agent.Tracking.cs src/Core/Agent.Tracking.cs
mv src/Core/V3/AgentCapabilities.cs src/Core/AgentCapabilities.cs
mv src/Core/V3/AgentConfiguration.cs src/Core/AgentConfiguration.cs
mv src/Core/V3/AgentEvent.cs src/Core/AgentEvent.cs
mv src/Core/V3/AgentMetadata.cs src/Core/AgentMetadata.cs
mv src/Core/V3/AgentResponse.cs src/Core/AgentResponse.cs
mv src/Core/V3/AgentState.cs src/Core/AgentState.cs
mv src/Core/V3/ChatMessage.cs src/Core/ChatMessage.cs
mv src/Core/V3/DurableChatHistoryProvider.cs src/Core/DurableChatHistoryProvider.cs
mv src/Core/V3/DynamicAgent.cs src/Core/DynamicAgent.cs
mv src/Core/V3/IAgent.cs src/Core/IAgent.cs
mv src/Core/V3/IDynamicAgent.cs src/Core/IDynamicAgent.cs
mv src/Core/V3/IEventDrivenAgent.cs src/Core/IEventDrivenAgent.cs
mv src/Core/V3/IObservableAgent.cs src/Core/IObservableAgent.cs
mv src/Core/V3/IStreamingAgent.cs src/Core/IStreamingAgent.cs
mv src/Core/V3/ITrackableAgent.cs src/Core/ITrackableAgent.cs
mv src/Core/V3/StateEntry.cs src/Core/StateEntry.cs
mv src/Core/V3/TrackingItem.cs src/Core/TrackingItem.cs
mv src/Core/V3/WeatherAgent.cs src/Core/WeatherAgent.cs

# Remove empty V3 directory
rmdir src/Core/V3
```

**Step 3: Rename memory keys — remove v3- prefix**

```bash
find src/Core -name "*.cs" -not -path "*/obj/*" -exec sed -i \
  -e 's/\[Memory("v3-history")\]/[Memory("history")]/g' \
  -e 's/\[Memory("v3-tracking")\]/[Memory("tracking")]/g' \
  {} +
```

**Step 4: Verify build compiles**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds (tests may reference old namespaces — we fix that next)

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: promote V3 to IAW.Core namespace, flatten directory structure"
```

### Task 5: Update Test Namespaces

**Files:**
- Modify: `test/Core.Tests/V3/AgentV3Tests.cs`
- Modify: `test/Core.Tests/V3/CoreAgentV3Tests.cs`
- Modify: `test/Core.Tests/V3/TestAgentV3.cs`
- Modify: `test/Core.Tests/V3/ArchitectureGuardV3Tests.cs`
- Modify: `test/Core.Tests/V3/MessageTypeTests.cs`
- Modify: `test/Core.Tests/V3/StreamNameTests.cs`
- Modify: `test/Core.Tests/V3/FileToolsTests.cs`
- Modify: `test/Core.Tests/V3/WorkspaceToolsTests.cs`

**Step 1: Update test namespaces and using statements**

```bash
find test/Core.Tests -name "*.cs" -not -path "*/obj/*" -exec sed -i \
  -e 's/using global::Core\.V3/using global::IAW.Core/g' \
  -e 's/using Core\.V3/using IAW.Core/g' \
  -e 's/namespace IAW\.Core\.Tests\.V3/namespace IAW.Core.Tests/g' \
  -e 's/global::Core\.V3/global::IAW.Core/g' \
  {} +
```

**Step 2: Move test files out of V3 subdirectory**

```bash
mv test/Core.Tests/V3/*.cs test/Core.Tests/
rmdir test/Core.Tests/V3
```

**Step 3: Rename test classes — remove V3 suffix**

In each test file, rename:
- `AgentV3Tests` → `AgentTests`
- `CoreAgentV3Tests` → `CoreAgentTests`
- `TestAgentV3` → `TestAgent`
- `ITestAgentV3` → `ITestAgent`
- `ArchitectureGuardV3Tests` → `ArchitectureGuardTests`
- `V3SiloConfigurator` → `TestSiloConfigurator`
- `V3ClientConfigurator` → `TestClientConfigurator`

```bash
find test/Core.Tests -name "*.cs" -not -path "*/obj/*" -exec sed -i \
  -e 's/AgentV3Tests/AgentTests/g' \
  -e 's/CoreAgentV3Tests/CoreAgentTests/g' \
  -e 's/ITestAgentV3/ITestAgent/g' \
  -e 's/TestAgentV3/TestAgent/g' \
  -e 's/ArchitectureGuardV3Tests/ArchitectureGuardTests/g' \
  -e 's/V3SiloConfigurator/TestSiloConfigurator/g' \
  -e 's/V3ClientConfigurator/TestClientConfigurator/g' \
  -e 's/AgentTestV3</AgentTest</g' \
  {} +
```

**Step 4: Rename test files**

```bash
mv test/Core.Tests/AgentV3Tests.cs test/Core.Tests/AgentTests.cs
mv test/Core.Tests/CoreAgentV3Tests.cs test/Core.Tests/CoreAgentTests.cs
mv test/Core.Tests/TestAgentV3.cs test/Core.Tests/TestAgent.cs
mv test/Core.Tests/ArchitectureGuardV3Tests.cs test/Core.Tests/ArchitectureGuardTests.cs
```

**Step 5: Update IAW.Testing — rename AgentTestV3 to AgentTest**

- Modify: `src/IAW.Testing/AgentTestV3.cs`

```bash
# Rename class and file
sed -i \
  -e 's/AgentTestV3/AgentTest/g' \
  -e 's/using Core\.V3/using IAW.Core/g' \
  -e 's/global::Core\.V3/global::IAW.Core/g' \
  -e 's/AgentTestV3SiloConfigurator/AgentTestSiloConfigurator/g' \
  -e 's/AgentTestV3ClientConfigurator/AgentTestClientConfigurator/g' \
  src/IAW.Testing/AgentTestV3.cs

mv src/IAW.Testing/AgentTestV3.cs src/IAW.Testing/AgentTest.cs
```

**Step 6: Build and run all tests**

Run: `dotnet build IAW.slnx && dotnet test IAW.slnx`
Expected: All tests pass

**Step 7: Commit**

```bash
git add -A
git commit -m "refactor: rename tests and testing framework to remove V3 suffix"
```

### Task 6: Update Consumer Projects (Samples, DevUI, MCP, Telegram)

**Files:**
- Modify: `samples/Samples/Samples.csproj` and all `.cs` files
- Modify: `src/DevUI/` all `.cs` files
- Modify: `src/IAW.MCP/` all `.cs` files
- Modify: `src/Clients.Telegram.Bot/` all `.cs` files
- Modify: `src/IAW.AppHost/` all `.cs` files

**Step 1: Update all consumer usings**

```bash
find samples src/DevUI src/IAW.MCP src/Clients.Telegram.Bot src/IAW.AppHost -name "*.cs" -not -path "*/obj/*" -exec sed -i \
  -e 's/using Core\.V3/using IAW.Core/g' \
  -e 's/using Core;/using IAW.Core;/g' \
  -e 's/using Core\.V2/using IAW.Core/g' \
  -e 's/global::Core\.V3/global::IAW.Core/g' \
  {} +
```

**Step 2: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds. Fix any remaining namespace references manually.

**Step 3: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All tests pass

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: update all consumer projects to IAW.Core namespace"
```

---

## Phase 3: Add Production Agent Features to Core

The production `E:\IAW\src\Core\` Agent has features that V3 needs for the ported agents to work. We need to add these to the V3 base class.

### Task 7: Add StateDescriptor Compatibility

The production agents use `StateDescriptor` (key + object value). V3 uses `StateEntry` (key + object value). These are structurally identical — we just need to verify `StateEntry` works the same way.

**Files:**
- Modify: `src/Core/StateEntry.cs` — verify it matches `StateDescriptor` semantics

**Step 1: Check StateEntry matches StateDescriptor**

Read both files. `StateDescriptor` from production:
```csharp
[GenerateSerializer]
public record StateDescriptor(
    [property: Id(0)] string Key,
    [property: Id(1)] object Value);
```

`StateEntry` from V3:
```csharp
[GenerateSerializer]
public record StateEntry(
    [property: Id(0)] string Key,
    [property: Id(1)] object Value);
```

These are identical in structure. No changes needed. The ported agents will use `StateEntry` directly.

**Step 2: Commit (no-op if nothing changed)**

### Task 8: Add PublishAsync (Untyped Event Publishing) to Agent Base

The production agents use `PublishAsync(eventName, payload, ct)` extensively. V3 has `PublishTypedAsync<T>` but needs the untyped version too.

**Files:**
- Modify: `src/Core/Agent.Events.cs`

**Step 1: Read current Agent.Events.cs**

Check if `PublishAsync` (untyped, taking string eventName + Dictionary payload) already exists.

**Step 2: Add PublishAsync if missing**

Add to `Agent.Events.cs`:

```csharp
public async Task PublishAsync(string eventName, Dictionary<string, object> payload, CancellationToken ct = default)
{
    var evt = new AgentEvent(eventName, this.GetPrimaryKeyString(), Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, payload);
    EventLog.Add(evt);
    await WriteStateAsync(ct);
    await PublishToStreamAsync(evt, ct);
}
```

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeds

**Step 4: Commit**

```bash
git add src/Core/Agent.Events.cs
git commit -m "feat: add untyped PublishAsync to Agent base class"
```

### Task 9: Add GetWorkspacePath and Workspace Helpers

Production agents use `GetWorkspacePath()` and `ValidatePathWithinWorkspace()`. Ensure these exist in V3's Agent.State.cs.

**Files:**
- Modify: `src/Core/Agent.State.cs`

**Step 1: Read Agent.State.cs and verify GetWorkspacePath exists**

V3 should already have `SetWorkspaceAsync` and `GetWorkspacePath`. Check if `ValidatePathWithinWorkspace` is there.

**Step 2: Add ValidatePathWithinWorkspace if missing**

```csharp
protected void ValidatePathWithinWorkspace(string path)
{
    var workspace = GetWorkspacePath();
    if (workspace is null) return;
    var fullPath = Path.GetFullPath(path);
    var fullWorkspace = Path.GetFullPath(workspace);
    if (!fullPath.StartsWith(fullWorkspace, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Path '{path}' is outside the workspace '{workspace}'.");
}
```

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add src/Core/Agent.State.cs
git commit -m "feat: add ValidatePathWithinWorkspace to Agent base"
```

### Task 10: Add HandleEvent Virtual Method

Production agents override `HandleEvent(AgentEvent, ct)`. V3 has `HandleEventAsync`. Ensure they align.

**Files:**
- Modify: `src/Core/Agent.Events.cs`

**Step 1: Read Agent.Events.cs**

Check if V3's `HandleEventAsync` matches what production agents expect (`HandleEvent`).

**Step 2: Add HandleEvent alias if production agents use different name**

If production uses `HandleEvent` and V3 uses `HandleEventAsync`, add:

```csharp
public virtual Task HandleEvent(AgentEvent agentEvent, CancellationToken ct = default)
    => HandleEventAsync(agentEvent, ct);
```

Or rename `HandleEventAsync` → `HandleEvent` if nothing else references it.

**Step 3: Build and test**

Run: `dotnet build src/Core/Core.csproj && dotnet test IAW.slnx`

**Step 4: Commit**

```bash
git add src/Core/Agent.Events.cs
git commit -m "feat: align HandleEvent naming with production agents"
```

### Task 11: Add SendMessage Streaming Method

Production PersonalAssistant uses `agent.SendMessage(chatMessage, ct)` which returns `IAsyncEnumerable<AgentResponse>`. V3 has `GetResponseStream` which returns `IAsyncEnumerable<string>`. We need to add `SendMessage` or adapt PersonalAssistant.

**Files:**
- Modify: `src/Core/Agent.cs`

**Step 1: Check what production Agent.Conversation.cs provides**

Read `E:\IAW\src\Core\Agent.Conversation.cs` to understand the `SendMessage` API.

**Step 2: Add SendMessage method to V3 Agent if needed**

The production `SendMessage` returns `IAsyncEnumerable<AgentResponse>` where `AgentResponse` has `Kind` (Text/Error/ToolCall) and `Content`. Add this to Agent.cs:

```csharp
public virtual async IAsyncEnumerable<AgentResponse> SendMessage(
    IAW.Core.ChatMessage message,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var chunk in GetResponseStream(message.Content, ct))
        yield return new AgentResponse(AgentResponseKind.Text, chunk);
}
```

Ensure `AgentResponse` and `AgentResponseKind` types exist (they should be in V3 already or need adding).

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add -A src/Core/
git commit -m "feat: add SendMessage streaming method to Agent base"
```

### Task 12: Add Marker Behavior Interfaces

Production agents implement `IConversationalAgent`, `IStatefulAgent`, `IEventDrivenAgent`, `ITrackableAgent`, `IObservableAgent`, `IStreamingAgent`. V3 has some of these as empty interfaces. Ensure they all exist.

**Files:**
- Verify/create: `src/Core/IConversationalAgent.cs`
- Verify existing: `src/Core/IEventDrivenAgent.cs`, `src/Core/IObservableAgent.cs`, `src/Core/IStreamingAgent.cs`, `src/Core/ITrackableAgent.cs`
- Create if missing: `src/Core/IStatefulAgent.cs`

**Step 1: Check which marker interfaces exist**

```bash
ls src/Core/I*Agent*.cs
```

**Step 2: Create any missing marker interfaces**

Each should be a simple empty interface extending `IAgent`:

```csharp
namespace IAW.Core;

public interface IConversationalAgent : IAgent;
public interface IStatefulAgent : IAgent;
```

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add -A src/Core/
git commit -m "feat: add all marker behavior interfaces"
```

### Task 13: Add DevVisibleAttribute

Production agents use `[DevVisible("description")]` to mark agents shown in DevUI. Add this attribute.

**Files:**
- Create: `src/Core/Attributes/DevVisibleAttribute.cs`

**Step 1: Create the attribute**

```csharp
namespace IAW.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DevVisibleAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}
```

**Step 2: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 3: Commit**

```bash
git add src/Core/Attributes/DevVisibleAttribute.cs
git commit -m "feat: add DevVisibleAttribute for DevUI agent discovery"
```

### Task 14: Add LlmAttribute to V3 Core

Production agents use `[Llm<Model>]` for IChatClient injection. This attribute + mapper need to exist in IAW.Core.

**Files:**
- Verify: `src/Core/AI/LlmAttribute.cs` and `src/Core/AI/LlmAttributeMapper.cs` exist

**Step 1: Check if AI directory was preserved**

These should already exist from the "keep shared infrastructure" step. Verify the namespace is correct (`IAW.Core.AI`).

**Step 2: Update namespace if needed**

```bash
find src/Core/AI -name "*.cs" -not -path "*/obj/*" -exec sed -i \
  -e 's/namespace Core\.AI/namespace IAW.Core.AI/g' \
  -e 's/using Core\.AI/using IAW.Core.AI/g' \
  -e 's/using Core;/using IAW.Core;/g' \
  {} +
```

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add -A src/Core/AI/
git commit -m "refactor: update AI namespace to IAW.Core.AI"
```

### Task 15: Add Orchestration Support Types

Production PlanningAgent uses `OrchestrationPlan`, `PlanStep`, `ScriptGenerator`, `ScriptExecutor`. Add these.

**Files:**
- Create: `src/Core/Orchestration/OrchestrationPlan.cs` (copy from `E:\IAW\src\Core\Orchestration\`)
- Create: `src/Core/Orchestration/ScriptGenerator.cs`
- Create: `src/Core/Orchestration/ScriptExecutor.cs`

**Step 1: Copy orchestration files from production**

```bash
mkdir -p src/Core/Orchestration
cp "E:/IAW/src/Core/Orchestration/OrchestrationPlan.cs" src/Core/Orchestration/
cp "E:/IAW/src/Core/Orchestration/ScriptGenerator.cs" src/Core/Orchestration/
cp "E:/IAW/src/Core/Orchestration/ScriptExecutor.cs" src/Core/Orchestration/
```

**Step 2: Update namespace**

```bash
find src/Core/Orchestration -name "*.cs" -exec sed -i \
  -e 's/namespace IAW\.Core\.Orchestration/namespace IAW.Core.Orchestration/g' \
  -e 's/using IAW\.Core/using IAW.Core/g' \
  {} +
```

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add -A src/Core/Orchestration/
git commit -m "feat: add orchestration support types from production"
```

### Task 16: Add AgentResponse and AgentResponseKind

Production streaming uses `AgentResponse(Kind, Content)` and `AgentResponseKind` enum.

**Files:**
- Verify/create: `src/Core/AgentResponse.cs`

**Step 1: Check if AgentResponse exists in V3**

If it doesn't exist or is different from production, create:

```csharp
namespace IAW.Core;

[GenerateSerializer]
public record AgentResponse(
    [property: Id(0)] AgentResponseKind Kind,
    [property: Id(1)] string Content);

public enum AgentResponseKind
{
    Text,
    Error,
    ToolCall,
    Metadata
}
```

**Step 2: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 3: Commit**

```bash
git add src/Core/AgentResponse.cs
git commit -m "feat: add AgentResponse and AgentResponseKind"
```

### Task 17: Add Communication Message Types Used by Production Agents

Production agents use `CodeChangedMessage`, `TestResultMessage`, `TaskAssignedMessage`, `AgentProgressUpdate` from `IAW.Core.Communication.Messages`.

**Files:**
- Create: `src/Core/Communication/Messages/CodeChangedMessage.cs`
- Create: `src/Core/Communication/Messages/TestResultMessage.cs`
- Create: `src/Core/Communication/Messages/TaskAssignedMessage.cs`
- Create: `src/Core/Communication/Messages/AgentProgressUpdate.cs`

**Step 1: Copy from production**

```bash
mkdir -p src/Core/Communication/Messages
cp E:/IAW/src/Core/Communication/Messages/*.cs src/Core/Communication/Messages/
```

**Step 2: Update namespaces**

```bash
find src/Core/Communication/Messages -name "*.cs" -exec sed -i \
  -e 's/namespace IAW\.Core\.Communication\.Messages/namespace IAW.Core.Communication.Messages/g' \
  {} +
```

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add -A src/Core/Communication/Messages/
git commit -m "feat: add production communication message types"
```

### Task 18: Add WorkspaceFiles Utility

Production agents use `WorkspaceFiles.EnumerateFilesAsync` and `WorkspaceFiles.CompareDirectoriesAsync`.

**Files:**
- Create: `src/Core/Tools/WorkspaceFiles.cs` (with `DirectoryComparison` and `FileDifference` records)

**Step 1: Copy from production**

```bash
cp "E:/IAW/src/Agents/Base/Infrastructure/WorkspaceFiles.cs" src/Core/Tools/
```

**Step 2: Update namespace to IAW.Core.Tools**

```bash
sed -i 's/namespace IAW\.Agents\.Infrastructure/namespace IAW.Core.Tools/g' src/Core/Tools/WorkspaceFiles.cs
```

**Step 3: Build**

Run: `dotnet build src/Core/Core.csproj`

**Step 4: Commit**

```bash
git add src/Core/Tools/WorkspaceFiles.cs
git commit -m "feat: add WorkspaceFiles utility to Core"
```

### Task 19: Build Verification Checkpoint

**Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All existing V3 tests pass

**Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve build issues after Phase 2-3 migration"
```

---

## Phase 4: Create IAW.Agents Project and Port Production Agents

### Task 20: Create IAW.Agents Project

**Files:**
- Create: `src/Agents/Agents.csproj`
- Create directory structure: `Infrastructure/`, `Orchestration/`, `Review/`, `Knowledge/`, `Messages/`

**Step 1: Create project**

```bash
mkdir -p src/Agents/Infrastructure src/Agents/Orchestration src/Agents/Review src/Agents/Knowledge src/Agents/Messages
```

Create `src/Agents/Agents.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>IAW.Agents</RootNamespace>
    <PackageId>IAW.Agents</PackageId>
    <Version>0.0.1</Version>
    <Description>Out-of-the-box agents for the IAW multi-agent runtime</Description>
    <Authors>IAW Contributors</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
  </ItemGroup>

</Project>
```

**Step 2: Add to solution**

```bash
dotnet sln IAW.slnx add src/Agents/Agents.csproj
```

**Step 3: Build**

Run: `dotnet build src/Agents/Agents.csproj`

**Step 4: Commit**

```bash
git add src/Agents/ IAW.slnx
git commit -m "feat: create IAW.Agents project"
```

### Task 21: Port Agent Messages

**Files:**
- Create: `src/Agents/Messages/BuildMetricsCollectedEvent.cs`
- Create: `src/Agents/Messages/CodeChangedEvent.cs`
- Create: `src/Agents/Messages/DeployFailedMessage.cs`
- Create: `src/Agents/Messages/DeploySucceededMessage.cs`
- Create: `src/Agents/Messages/ImprovementProposalMessage.cs`
- Create: `src/Agents/Messages/ReviewCompletedMessage.cs`
- Create: `src/Agents/Messages/ReviewFeedbackMessage.cs`
- Create: `src/Agents/Messages/SpecReadyEvent.cs`
- Create: `src/Agents/Messages/TaskCompletedMessage.cs`
- Create: `src/Agents/Messages/TaskFailedMessage.cs`
- Create: `src/Agents/Messages/TestsPassedEvent.cs`

**Step 1: Copy all message files from production**

```bash
cp E:/IAW/src/Agents/Base/Messages/*.cs src/Agents/Messages/
```

**Step 2: Build**

Run: `dotnet build src/Agents/Agents.csproj`

**Step 3: Commit**

```bash
git add src/Agents/Messages/
git commit -m "feat: port production agent message types"
```

### Task 22: Port Infrastructure Agent Interfaces

**Files:**
- Create: `src/Agents/Infrastructure/IFileSystem.cs` (with FileAccessMetrics)
- Create: `src/Agents/Infrastructure/IShell.cs` (with CommandResult, ShellMetrics)
- Create: `src/Agents/Infrastructure/IGit.cs` (with GitMetrics)
- Create: `src/Agents/Infrastructure/IBuild.cs` (with BuildResult, TestResult, BuildMetrics)
- Create: `src/Agents/Infrastructure/IAspire.cs` (with ResourceStatus, AspireMetrics)

**Step 1: Copy interface files from production**

```bash
cp E:/IAW/src/Agents/Base/Infrastructure/IFileSystem.cs src/Agents/Infrastructure/
cp E:/IAW/src/Agents/Base/Infrastructure/IShell.cs src/Agents/Infrastructure/
cp E:/IAW/src/Agents/Base/Infrastructure/IGit.cs src/Agents/Infrastructure/
cp E:/IAW/src/Agents/Base/Infrastructure/IBuild.cs src/Agents/Infrastructure/
cp E:/IAW/src/Agents/Base/Infrastructure/IAspire.cs src/Agents/Infrastructure/
```

**Step 2: Update IAgent reference from `IAW.Core.IAgent` (was `using IAW.Core;`)**

```bash
find src/Agents/Infrastructure -name "I*.cs" -exec sed -i \
  -e 's/using IAW\.Core;/using IAW.Core;/g' \
  {} +
```

The files already use `IAW.Core` namespace for IAgent — should just work.

**Step 3: Build**

Run: `dotnet build src/Agents/Agents.csproj`

**Step 4: Commit**

```bash
git add src/Agents/Infrastructure/
git commit -m "feat: port infrastructure agent interfaces"
```

### Task 23: Port FileSystemAgent

**Files:**
- Create: `src/Agents/Infrastructure/FileSystemAgent.cs`

**Step 1: Copy from production and adapt to V3 constructor**

Copy `E:\IAW\src\Agents\Base\Infrastructure\FileSystemAgent.cs` to `src/Agents/Infrastructure/`.

Apply these transformations:
1. Change base class call: `Agent(state, eventLog, trackingItems, chatClient)` → `Agent(state, eventLog, chatClient, history, trackingItems)`
2. Add `[Memory("history")] IDurableList<ChatMessage> history` parameter
3. Replace `StateDescriptor` → `StateEntry`
4. Replace `SystemPrompt` property → `Instructions`
5. Drop `IConversationalAgent, IStatefulAgent` markers (or keep if they exist as empty interfaces)
6. Add `[GrainType("file-system")]`
7. Add `using IAW.Core.Tools;` for WorkspaceFiles
8. Change `using Microsoft.Extensions.AI;` for ChatMessage

**Step 2: Build**

Run: `dotnet build src/Agents/Agents.csproj`

**Step 3: Commit**

```bash
git add src/Agents/Infrastructure/FileSystemAgent.cs
git commit -m "feat: port FileSystemAgent to V3"
```

### Task 24: Port ShellAgent

Same pattern as Task 23. Copy, adapt constructor, `StateDescriptor` → `StateEntry`, `SystemPrompt` → `Instructions`, add `[GrainType("shell")]`.

**Files:**
- Create: `src/Agents/Infrastructure/ShellAgent.cs`

### Task 25: Port GitAgent

Same pattern. Add `[GrainType("git")]`.

**Files:**
- Create: `src/Agents/Infrastructure/GitAgent.cs`

### Task 26: Port BuildAgent

Same pattern. Add `[GrainType("build")]`. Note: `BuildAgent` is `partial class` (has `[GeneratedRegex]`).

**Files:**
- Create: `src/Agents/Infrastructure/BuildAgent.cs`

### Task 27: Port AspireAgent

Same pattern. Add `[GrainType("aspire")]`. Drop `IEventDrivenAgent, ITrackableAgent, IStreamingAgent, IStatefulAgent` markers (keep as empty interfaces or drop entirely).

**Files:**
- Create: `src/Agents/Infrastructure/AspireAgent.cs`

### Task 28: Port Orchestration Agent Interfaces

**Files:**
- Create: `src/Agents/Orchestration/IPersonalAssistant.cs`
- Create: `src/Agents/Orchestration/IPlanning.cs`
- Create: `src/Agents/Orchestration/INotification.cs`
- Create: `src/Agents/Orchestration/IDeployer.cs`

**Step 1: Copy from production**

```bash
cp E:/IAW/src/Agents/Base/Orchestration/IPersonalAssistant.cs src/Agents/Orchestration/
cp E:/IAW/src/Agents/Base/Orchestration/IPlanning.cs src/Agents/Orchestration/
cp E:/IAW/src/Agents/Base/Orchestration/INotification.cs src/Agents/Orchestration/
cp E:/IAW/src/Agents/Base/Orchestration/IDeployer.cs src/Agents/Orchestration/
```

**Step 2: Build**

Run: `dotnet build src/Agents/Agents.csproj`

**Step 3: Commit**

```bash
git add src/Agents/Orchestration/
git commit -m "feat: port orchestration agent interfaces"
```

### Task 29: Port PersonalAssistantAgent

The most complex agent. Uses `IReceiver<T>` for 4 message types, `ResolveAgent`, `SpawnDynamicAgent`, `AssignTaskToAgent`.

**Files:**
- Create: `src/Agents/Orchestration/PersonalAssistantAgent.cs`

**Step 1: Copy from production and apply V3 constructor transformation**

Key changes:
- V3 constructor: `Agent(state, eventLog, chatClient, history, trackingItems)`
- Add `[Memory("history")] IDurableList<ChatMessage> history`
- `StateDescriptor` → `StateEntry`
- `SystemPrompt` → `Instructions`
- Add `[GrainType("personal-assistant")]`
- `IReceiver<T>.Receive(msg, ct)` → `IReceiver<T>.ReceiveAsync(msg, ct)` (V3 naming)
- `IReceiver<T>.CanReceive(ct)` → `IReceiver<T>.CanReceiveAsync(ct)` (V3 naming)
- Update `MessageReceipt` constructor to match V3 (may need 4th param `null`)

**Step 2: Build**

Run: `dotnet build src/Agents/Agents.csproj`

**Step 3: Commit**

```bash
git add src/Agents/Orchestration/PersonalAssistantAgent.cs
git commit -m "feat: port PersonalAssistantAgent to V3"
```

### Task 30: Port PlanningAgent

**Files:**
- Create: `src/Agents/Orchestration/PlanningAgent.cs`

Uses `OrchestrationPlan`, `ScriptGenerator`, `ScriptExecutor` from Task 15.

### Task 31: Port NotificationAgent

Simplest agent. Override `HandleEvent`.

**Files:**
- Create: `src/Agents/Orchestration/NotificationAgent.cs`

### Task 32: Port DeployerAgent

Uses `IStreamConsumer<TestsPassedEvent>`, cross-agent calls to IBuild, IAspire, IGit.

**Files:**
- Create: `src/Agents/Orchestration/DeployerAgent.cs`

### Task 33: Port Review Agent Interfaces

**Files:**
- Create: `src/Agents/Review/IReviewer.cs`
- Create: `src/Agents/Review/ISelfImprovement.cs`

### Task 34: Port ReviewerAgent

Uses `IStreamConsumer<CodeChangedEvent>`, cross-agent calls to IFileSystem.

**Files:**
- Create: `src/Agents/Review/ReviewerAgent.cs`

### Task 35: Port SelfImprovementAgent

Most complex review agent. Uses `IReceiver<ReviewCompletedMessage>`, `IStreamConsumer<TestsPassedEvent>`, `IStreamConsumer<CodeChangedEvent>`, cross-agent calls to IFileSystem, IBuild, IGit.

**Files:**
- Create: `src/Agents/Review/SelfImprovementAgent.cs`

### Task 36: Port Knowledge Agent Interfaces

**Files:**
- Create: `src/Agents/Knowledge/IKnowledge.cs` (with ProjectInfo, ProjectDecision, ProjectPattern records)
- Create: `src/Agents/Knowledge/IUser.cs`

### Task 37: Port KnowledgeAgent

**Files:**
- Create: `src/Agents/Knowledge/KnowledgeAgent.cs`

### Task 38: Port UserAgent

**Files:**
- Create: `src/Agents/Knowledge/UserAgent.cs`

### Task 39: Build Verification — All 14 Agents

**Step 1: Build Agents project**

Run: `dotnet build src/Agents/Agents.csproj`
Expected: Build succeeds with all 14 agents

**Step 2: Full solution build**

Run: `dotnet build IAW.slnx`

**Step 3: Commit any fixes**

```bash
git add -A
git commit -m "feat: all 14 base agents ported to V3"
```

---

## Phase 5: Create IAW.Agents.CSharp Project

### Task 40: Create IAW.Agents.CSharp Project

**Files:**
- Create: `src/Agents.CSharp/Agents.CSharp.csproj`
- Create: `src/Agents.CSharp/Tools/` directory

**Step 1: Create project**

```bash
mkdir -p src/Agents.CSharp/Tools src/Agents.CSharp/Models src/Agents.CSharp/Prompts
```

Create `src/Agents.CSharp/Agents.CSharp.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <RootNamespace>IAW.Agents.CSharp</RootNamespace>
    <PackageId>IAW.Agents.CSharp</PackageId>
    <Version>0.0.1</Version>
    <Description>C# development agents (Roslyn, DotNet, NuGet, GitHub) for IAW</Description>
    <Authors>IAW Contributors</Authors>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
    <ProjectReference Include="..\Agents\Agents.csproj" />
  </ItemGroup>

</Project>
```

**Step 2: Add Roslyn and Octokit NuGet packages**

Check `Directory.Packages.props` for existing versions, add if missing:
- `Microsoft.CodeAnalysis.CSharp`
- `Octokit`

**Step 3: Add to solution**

```bash
dotnet sln IAW.slnx add src/Agents.CSharp/Agents.CSharp.csproj
```

**Step 4: Commit**

```bash
git add src/Agents.CSharp/ IAW.slnx
git commit -m "feat: create IAW.Agents.CSharp project"
```

### Task 41: Port CSharp Agent Interfaces

**Files:**
- Create: `src/Agents.CSharp/IRoslyn.cs`
- Create: `src/Agents.CSharp/IDotNet.cs` (with TestRunResult, FormatResult)
- Create: `src/Agents.CSharp/INuGet.cs`
- Create: `src/Agents.CSharp/IGitHub.cs`
- Create: `src/Agents.CSharp/Models/PackageUpdate.cs`
- Create: `src/Agents.CSharp/Models/ReleaseInfo.cs`

**Step 1: Copy from production**

```bash
cp E:/IAW/src/Agents/CSharp/IRoslyn.cs src/Agents.CSharp/
cp E:/IAW/src/Agents/CSharp/IDotNet.cs src/Agents.CSharp/
cp E:/IAW/src/Agents/CSharp/INuGet.cs src/Agents.CSharp/
cp E:/IAW/src/Agents/CSharp/IGitHub.cs src/Agents.CSharp/
cp E:/IAW/src/Agents/CSharp/Models/*.cs src/Agents.CSharp/Models/
```

**Step 2: Build**

**Step 3: Commit**

### Task 42: Port RoslynAgent

**Files:**
- Create: `src/Agents.CSharp/RoslynAgent.cs`
- Create: `src/Agents.CSharp/Tools/RoslynTools.cs`

Copy from production, apply V3 constructor transformation. Most complex CSharp agent with Roslyn parsing.

### Task 43: Port DotNetAgent

**Files:**
- Create: `src/Agents.CSharp/DotNetAgent.cs`

### Task 44: Port NuGetAgent

**Files:**
- Create: `src/Agents.CSharp/NuGetAgent.cs`

Uses `ILocalDurableJobManager` for scheduled checks.

### Task 45: Port GitHubAgent

**Files:**
- Create: `src/Agents.CSharp/GitHubAgent.cs`

Uses `IGitHubClient` (Octokit) and `ILocalDurableJobManager`.

### Task 46: Port CSharp Supporting Files

**Files:**
- Create: `src/Agents.CSharp/Prompts/CodingAgentPrompts.cs`

### Task 47: Build Verification — All 18 Agents

**Step 1: Build CSharp project**

Run: `dotnet build src/Agents.CSharp/Agents.CSharp.csproj`

**Step 2: Full solution build**

Run: `dotnet build IAW.slnx`

**Step 3: Run all tests**

Run: `dotnet test IAW.slnx`

**Step 4: Commit**

```bash
git add -A
git commit -m "feat: all 18 agents ported — 14 base + 4 CSharp"
```

---

## Phase 6: Agent Tests

### Task 48: Add Agent Test Classes

Each agent gets a single-line test class inheriting `AgentTest<T>`.

**Files:**
- Create: `test/Core.Tests/Agents/FileSystemAgentTests.cs`
- Create: `test/Core.Tests/Agents/ShellAgentTests.cs`
- Create: `test/Core.Tests/Agents/GitAgentTests.cs`
- Create: `test/Core.Tests/Agents/BuildAgentTests.cs`
- Create: `test/Core.Tests/Agents/AspireAgentTests.cs`
- Create: `test/Core.Tests/Agents/PersonalAssistantAgentTests.cs`
- Create: `test/Core.Tests/Agents/PlanningAgentTests.cs`
- Create: `test/Core.Tests/Agents/NotificationAgentTests.cs`
- Create: `test/Core.Tests/Agents/DeployerAgentTests.cs`
- Create: `test/Core.Tests/Agents/ReviewerAgentTests.cs`
- Create: `test/Core.Tests/Agents/SelfImprovementAgentTests.cs`
- Create: `test/Core.Tests/Agents/KnowledgeAgentTests.cs`
- Create: `test/Core.Tests/Agents/UserAgentTests.cs`
- Create: `test/Core.Tests/Agents/RoslynAgentTests.cs`
- Create: `test/Core.Tests/Agents/DotNetAgentTests.cs`
- Create: `test/Core.Tests/Agents/NuGetAgentTests.cs`
- Create: `test/Core.Tests/Agents/GitHubAgentTests.cs`

Each file follows this pattern:

```csharp
using IAW.Agents.Infrastructure;
using IAW.Testing;

namespace IAW.Core.Tests.Agents;

public class FileSystemAgentTests : AgentTest<FileSystemAgent>;
```

**Step 1: Create test directory and files**

```bash
mkdir -p test/Core.Tests/Agents
```

Create each test file with the single-line pattern.

**Step 2: Add project references to test project**

Add to `test/Core.Tests/IAW.Core.Tests.csproj`:
```xml
<ProjectReference Include="..\..\src\Agents\Agents.csproj" />
<ProjectReference Include="..\..\src\Agents.CSharp\Agents.CSharp.csproj" />
```

**Step 3: Build and run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All AgentTest<T> auto-generated tests pass for all 17 agents (18 minus any that need special DI like IHttpClientFactory).

Note: Agents that need `IHttpClientFactory`, `IGitHubClient`, or `ILocalDurableJobManager` may need test silo configurator updates to register mock services.

**Step 4: Update test silo configurator for extra DI**

In the test silo configurator, add:
```csharp
siloBuilder.Services.AddSingleton<IHttpClientFactory>(new MockHttpClientFactory());
siloBuilder.Services.AddSingleton<IGitHubClient>(new MockGitHubClient());
```

**Step 5: Run tests again**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`
Expected: All tests pass

**Step 6: Commit**

```bash
git add -A test/
git commit -m "feat: add agent test classes for all 18 agents"
```

---

## Phase 7: Move Sample Agents

### Task 49: Move V3 Sample Agents to samples/

**Files:**
- Move: `src/Core/Samples/CodeReviewAgent.cs` → `samples/Samples/Agents/CodeReviewAgent.cs`
- Move: `src/Core/Samples/CIPipelineAgent.cs` → `samples/Samples/Agents/CIPipelineAgent.cs`
- Move: `src/Core/Samples/InfraMonitorAgent.cs` → `samples/Samples/Agents/InfraMonitorAgent.cs`
- Move: `src/Core/Samples/PersonalAssistantAgent.cs` → `samples/Samples/Agents/PersonalAssistantSampleAgent.cs`
- Move: `src/Core/Samples/KnowledgeBaseAgent.cs` → `samples/Samples/Agents/KnowledgeBaseSampleAgent.cs`
- Move: `src/Core/WeatherAgent.cs` → `samples/Samples/Agents/WeatherAgent.cs`

**Step 1: Move files**

```bash
mkdir -p samples/Samples/Agents
mv src/Core/Samples/*.cs samples/Samples/Agents/
mv src/Core/WeatherAgent.cs samples/Samples/Agents/
rmdir src/Core/Samples
```

**Step 2: Update namespaces**

```bash
find samples/Samples/Agents -name "*.cs" -exec sed -i \
  -e 's/namespace IAW\.Core\.Samples/namespace IAW.Samples.Agents/g' \
  -e 's/namespace IAW\.Core/namespace IAW.Samples.Agents/g' \
  {} +
```

**Step 3: Build**

Run: `dotnet build IAW.slnx`

**Step 4: Commit**

```bash
git add -A
git commit -m "refactor: move sample agents from Core to samples/"
```

---

## Phase 8: Update Architecture Guards

### Task 50: Update Architecture Guard Tests

**Files:**
- Modify: `test/Core.Tests/ArchitectureGuardTests.cs`

Update reflection-based guards to:
1. Look for `IAW.Core` namespace instead of `Core.V3`
2. Add guard: all agents in IAW.Agents extend `IAW.Core.Agent`
3. Add guard: no V1/V2 types exist
4. Add guard: all serializable records have `[GenerateSerializer]`

**Step 1: Update guard tests**

**Step 2: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "ArchitectureGuard"`

**Step 3: Commit**

```bash
git add test/Core.Tests/ArchitectureGuardTests.cs
git commit -m "refactor: update architecture guards for IAW.Core namespace"
```

---

## Phase 9: NuGet Packaging and Documentation

### Task 51: Update Core.csproj NuGet Metadata

**Files:**
- Modify: `src/Core/Core.csproj`

Update version to 0.0.1, add NuGet metadata:

```xml
<PropertyGroup>
  <PackageId>IAW.Core</PackageId>
  <Version>0.0.1</Version>
  <Description>Orleans-based multi-agent runtime for .NET</Description>
  <Authors>IAW Contributors</Authors>
  <PackageLicenseExpression>MIT</PackageLicenseExpression>
  <RepositoryUrl>https://github.com/AINovelist/IAW</RepositoryUrl>
  <RootNamespace>IAW.Core</RootNamespace>
</PropertyGroup>
```

### Task 52: Update IAW.Testing NuGet Metadata

**Files:**
- Modify: `src/IAW.Testing/IAW.Testing.csproj`

```xml
<PropertyGroup>
  <PackageId>IAW.Testing</PackageId>
  <Version>0.0.1</Version>
  <Description>Testing framework for IAW agents — one-line test inheritance</Description>
</PropertyGroup>
```

### Task 53: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

Remove all V1/V2 references. Update:
- Architecture section: describe V3 as the only agent model
- Agent list: reference 18 out-of-the-box agents
- Package list: IAW.Core, IAW.Agents, IAW.Agents.CSharp, IAW.Testing
- Namespace: `IAW.Core` throughout
- Build commands remain the same

### Task 54: Update README.md

**Files:**
- Modify: `README.md`

Public-facing README for open source:
- Quick start with `dotnet add package IAW.Core`
- Agent creation example
- List of out-of-the-box agents
- Link to documentation

### Task 55: Final Build and Test

**Step 1: Full solution build**

Run: `dotnet build IAW.slnx`
Expected: Zero errors

**Step 2: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All tests pass

**Step 3: Commit everything**

```bash
git add -A
git commit -m "docs: update documentation for v0.0.1 public release"
```

---

## Summary

| Phase | Tasks | Description |
|-------|-------|-------------|
| 1 | 1-3 | Delete V1/V2 code and tests |
| 2 | 4-6 | Rename V3 namespaces to IAW.Core |
| 3 | 7-18 | Add production features to Core base class |
| 4 | 20-39 | Create IAW.Agents, port 14 base agents |
| 5 | 40-47 | Create IAW.Agents.CSharp, port 4 C# agents |
| 6 | 48 | Add agent test classes |
| 7 | 49 | Move sample agents to samples/ |
| 8 | 50 | Update architecture guards |
| 9 | 51-55 | NuGet packaging and documentation |
