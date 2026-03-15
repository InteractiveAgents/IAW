# Orchestration Events + Enhanced Plan Model + Checkpoints — Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add orchestration event types (progress, error, artifact, completed), enhance OrchestrationPlan with task/project scoping and step criticality, and add blob-based checkpoint persistence — the foundation all other orchestration features depend on.

**Architecture:** New event record types in `Core.Orchestration` implement `IEvent` for Orleans stream compatibility. `OrchestrationPlan` gains `TaskId`, `ProjectId`, and step-level `Critical` flag. `CheckpointStore` wraps `BlobFileStorage` for saving/loading step results as JSON blobs keyed by `orchestration/{taskId}/step-{N}.json`.

**Tech Stack:** Orleans Streams, Azure Blob Storage (via existing `BlobFileStorage`), System.Text.Json

**Spec:** `docs/superpowers/specs/2026-03-15-autonomous-orchestration-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Core/Orchestration/OrchestrationEvents.cs` | Create | 4 event records: Progress, Error, Artifact, Completed |
| `src/Core/Orchestration/OrchestrationPlan.cs` | Modify | Add TaskId, ProjectId, GlobalParameters to plan; add Critical flag to PlanStep |
| `src/Core/Orchestration/CheckpointStore.cs` | Create | Save/load step results to blob storage |
| `src/Core/Orchestration/OrchestrationStatus.cs` | Modify | Add `SelfHealing` status for recovery phase |
| `test/Core.Tests/Orchestration/OrchestrationEventsTests.cs` | Create | Event construction and serialization tests |
| `test/Core.Tests/Orchestration/OrchestrationPlanTests.cs` | Create | Enhanced plan model tests |

---

## Chunk 1: Orchestration Events

### Task 1: Create orchestration event types

**Files:**
- Create: `src/Core/Orchestration/OrchestrationEvents.cs`

- [ ] **Step 1: Create the 4 event records**

```csharp
// src/Core/Orchestration/OrchestrationEvents.cs
using Core.Communication;

namespace Core.Orchestration;

[GenerateSerializer]
public record OrchestrationProgressEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] int StepIndex,
    [property: Id(2)] string Message,
    [property: Id(3)] DateTimeOffset Timestamp) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
}

[GenerateSerializer]
public record OrchestrationErrorEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] int StepIndex,
    [property: Id(2)] string ErrorType,
    [property: Id(3)] string ErrorMessage,
    [property: Id(4)] DateTimeOffset Timestamp) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
}

[GenerateSerializer]
public record OrchestrationArtifactEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string BlobPath,
    [property: Id(2)] string FileName,
    [property: Id(3)] string MimeType) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
    public DateTimeOffset Timestamp => DateTimeOffset.UtcNow;
}

[GenerateSerializer]
public record OrchestrationCompletedEvent(
    [property: Id(0)] string TaskId,
    [property: Id(1)] string Summary,
    [property: Id(2)] IReadOnlyList<string> ArtifactPaths,
    [property: Id(3)] DateTimeOffset Timestamp) : IEvent
{
    public string SourceAgentId => TaskId;
    public string CorrelationId => TaskId;
}
```

- [ ] **Step 2: Verify IEvent interface is in Core.Communication**

Check that `IEvent` exists at `src/Core/Communication/IEvent.cs` and has `SourceAgentId`, `CorrelationId`, `Timestamp` properties. If not, check the actual interface name and adapt.

Run: `dotnet build src/Core/Core.csproj`

- [ ] **Step 3: Commit**

```bash
git add src/Core/Orchestration/OrchestrationEvents.cs
git commit -m "feat: add orchestration event types (Progress, Error, Artifact, Completed)"
```

### Task 2: Write event tests

**Files:**
- Create: `test/Core.Tests/Orchestration/OrchestrationEventsTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// test/Core.Tests/Orchestration/OrchestrationEventsTests.cs
using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class OrchestrationEventsTests
{
    [Fact]
    public void ProgressEvent_SetsTaskIdAsSourceAgent()
    {
        var evt = new OrchestrationProgressEvent("task-1", 0, "Working...", DateTimeOffset.UtcNow);
        Assert.Equal("task-1", evt.SourceAgentId);
        Assert.Equal("task-1", evt.CorrelationId);
    }

    [Fact]
    public void ErrorEvent_CapturesErrorDetails()
    {
        var evt = new OrchestrationErrorEvent("task-1", 2, "TimeoutException", "Connection timed out", DateTimeOffset.UtcNow);
        Assert.Equal(2, evt.StepIndex);
        Assert.Equal("TimeoutException", evt.ErrorType);
        Assert.Equal("Connection timed out", evt.ErrorMessage);
    }

    [Fact]
    public void ArtifactEvent_StoresBlobPath()
    {
        var evt = new OrchestrationArtifactEvent("task-1", "orchestration/task-1/report.xlsx", "report.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        Assert.Equal("orchestration/task-1/report.xlsx", evt.BlobPath);
        Assert.Equal("report.xlsx", evt.FileName);
    }

    [Fact]
    public void CompletedEvent_ContainsArtifactPaths()
    {
        var evt = new OrchestrationCompletedEvent("task-1", "Done", ["path1", "path2"], DateTimeOffset.UtcNow);
        Assert.Equal(2, evt.ArtifactPaths.Count);
        Assert.Equal("Done", evt.Summary);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OrchestrationEventsTests" -v minimal`

- [ ] **Step 3: Commit**

```bash
git add test/Core.Tests/Orchestration/OrchestrationEventsTests.cs
git commit -m "test: add orchestration event tests"
```

---

## Chunk 2: Enhanced OrchestrationPlan

### Task 3: Extend OrchestrationPlan and PlanStep

**Files:**
- Modify: `src/Core/Orchestration/OrchestrationPlan.cs`

- [ ] **Step 1: Read the current file, then replace with enhanced version**

Current file has `OrchestrationPlan(Summary, Steps)` and `PlanStep(Order, AgentType, Action, Parameters)`.

Replace entire file:

```csharp
// src/Core/Orchestration/OrchestrationPlan.cs
namespace Core.Orchestration;

[GenerateSerializer]
public record OrchestrationPlan(
    [property: Id(0)] string Summary,
    [property: Id(1)] IReadOnlyList<PlanStep> Steps,
    [property: Id(2)] string TaskId = "",
    [property: Id(3)] string ProjectId = "",
    [property: Id(4)] Dictionary<string, string>? GlobalParameters = null);

[GenerateSerializer]
public record PlanStep(
    [property: Id(0)] int Order,
    [property: Id(1)] string AgentType,
    [property: Id(2)] string Action,
    [property: Id(3)] Dictionary<string, string> Parameters,
    [property: Id(4)] bool Critical = true);
```

- [ ] **Step 2: Build to verify backward compat**

Run: `dotnet build src/Core/Core.csproj`

All existing code that creates `OrchestrationPlan(summary, steps)` or `PlanStep(order, agentType, action, parameters)` should still compile because new fields have defaults.

- [ ] **Step 3: Commit**

```bash
git add src/Core/Orchestration/OrchestrationPlan.cs
git commit -m "feat: extend OrchestrationPlan with TaskId, ProjectId, and step Critical flag"
```

### Task 4: Add SelfHealing status

**Files:**
- Modify: `src/Core/Orchestration/OrchestrationStatus.cs`

- [ ] **Step 1: Add SelfHealing to OrchestrationStatus enum**

Add `SelfHealing` after `Recovering` in the enum:

```csharp
public enum OrchestrationStatus
{
    Created,
    Running,
    Paused,
    Completed,
    Failed,
    Recovering,
    SelfHealing
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Core/Orchestration/OrchestrationStatus.cs
git commit -m "feat: add SelfHealing status to OrchestrationStatus"
```

### Task 5: Write plan model tests

**Files:**
- Create: `test/Core.Tests/Orchestration/OrchestrationPlanTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// test/Core.Tests/Orchestration/OrchestrationPlanTests.cs
using Core.Orchestration;
using Xunit;

namespace IAW.Core.Tests.Orchestration;

public class OrchestrationPlanTests
{
    [Fact]
    public void Plan_BackwardCompat_TwoArgConstructor()
    {
        var plan = new OrchestrationPlan("Test", []);
        Assert.Equal("", plan.TaskId);
        Assert.Equal("", plan.ProjectId);
        Assert.Null(plan.GlobalParameters);
    }

    [Fact]
    public void Plan_WithTaskAndProject()
    {
        var plan = new OrchestrationPlan("Test", [], TaskId: "task-123", ProjectId: "user/general");
        Assert.Equal("task-123", plan.TaskId);
        Assert.Equal("user/general", plan.ProjectId);
    }

    [Fact]
    public void PlanStep_DefaultCritical_IsTrue()
    {
        var step = new PlanStep(1, "IFileSystem", "ReadFileAsync", new Dictionary<string, string>());
        Assert.True(step.Critical);
    }

    [Fact]
    public void PlanStep_ExplicitNonCritical()
    {
        var step = new PlanStep(1, "IWebSearch", "SearchAsync", new Dictionary<string, string>(), Critical: false);
        Assert.False(step.Critical);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~OrchestrationPlanTests" -v minimal`

- [ ] **Step 3: Commit**

```bash
git add test/Core.Tests/Orchestration/OrchestrationPlanTests.cs
git commit -m "test: add OrchestrationPlan model tests"
```

---

## Chunk 3: Checkpoint Store

### Task 6: Implement CheckpointStore

**Files:**
- Create: `src/Core/Orchestration/CheckpointStore.cs`

- [ ] **Step 1: Create CheckpointStore**

```csharp
// src/Core/Orchestration/CheckpointStore.cs
using System.Text;
using System.Text.Json;
using Core.Services;

namespace Core.Orchestration;

public sealed class CheckpointStore(BlobFileStorage blobStorage)
{
    public async Task SaveAsync(string taskId, int stepIndex, object result, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(result);
        var path = BuildPath(taskId, stepIndex);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await blobStorage.UploadAsync(stream, path, "application/json");
    }

    public async Task<string?> LoadAsync(string taskId, int stepIndex, CancellationToken ct = default)
    {
        var path = BuildPath(taskId, stepIndex);
        try
        {
            await using var stream = await blobStorage.DownloadAsync(BuildBlobUri(taskId, stepIndex));
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveArtifactAsync(string taskId, Stream content, string fileName, string mimeType, CancellationToken ct = default)
    {
        var path = $"orchestration/{taskId}/{fileName}";
        await blobStorage.UploadAsync(content, path, mimeType);
    }

    static string BuildPath(string taskId, int stepIndex) =>
        $"orchestration/{taskId}/step-{stepIndex}.json";

    static string BuildBlobUri(string taskId, int stepIndex) =>
        BuildPath(taskId, stepIndex);
}
```

Note: `BlobFileStorage.DownloadAsync` takes a blob URI. The `BuildBlobUri` method may need adjustment based on how `BlobFileStorage` resolves paths vs URIs. Check the implementation — if `DownloadAsync` expects a full URI, you'll need to construct it differently. If it accepts a relative blob name, this works as-is.

- [ ] **Step 2: Build**

Run: `dotnet build src/Core/Core.csproj`

- [ ] **Step 3: Commit**

```bash
git add src/Core/Orchestration/CheckpointStore.cs
git commit -m "feat: add CheckpointStore for orchestration step result persistence"
```

### Task 7: Run full test suite

- [ ] **Step 1: Build everything**

Run: `dotnet build IAW.slnx`

- [ ] **Step 2: Run all tests**

Run: `dotnet test test/Core.Tests --verbosity minimal --filter "FullyQualifiedName!~StreamPublish_MultipleConsumers"`
Expected: All pass including new orchestration tests

- [ ] **Step 3: Push**

```bash
git push origin v3
```

---

## Summary

| Component | What | Why |
|-----------|------|-----|
| `OrchestrationEvents.cs` | 4 event types implementing IEvent | Orleans stream communication between scripts, supervisor, and Telegram |
| `OrchestrationPlan.cs` | TaskId, ProjectId, GlobalParameters, Critical flag | Scoping plans to tasks/projects, marking non-critical steps for skip-on-failure |
| `OrchestrationStatus.cs` | SelfHealing enum value | Track when supervisor is attempting LLM-based recovery |
| `CheckpointStore.cs` | Blob-based step result persistence | Self-healing can resume from last good checkpoint instead of restarting |
