# Telegram Bot Refactor Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the IAW Telegram bot into a multi-tenant, project-based platform with multimodal messages, RAG, dynamic UI, and async event-driven updates.

**Architecture:** Six vertical slices, each end-to-end shippable. Core model changes first (multimodal ChatMessage, Project grain), then interactive UI (UISession, approvals), then file/RAG infrastructure, then dashboard/tasks, then context management, then full dynamic UI widgets.

**Tech Stack:** Orleans 10.0 journaled grains, Telegram.BotAPI 9.5.0, PdfPig, Qdrant, Azurite, Microsoft.Extensions.AI, Microsoft.SemanticKernel.Core (TextChunker)

**Spec:** `docs/superpowers/specs/2026-03-14-telegram-bot-refactor-design.md`

### Codebase Notes (read before implementing)

1. **Namespaces:** Source files under `src/Core/` use root namespaces like `Core.Contracts`, `Core.Agents`, `Core.AI` (NOT `IAW.Core.*`). Tests use `IAW.Core.Tests`. Agents use `IAW.Agents.*`. Code snippets in this plan may show `IAW.Core.Contracts` — always use the actual codebase namespace `Core.Contracts` etc.
2. **AgentDurableState is sealed:** Before creating `ProjectDurableState : AgentDurableState`, unseal `AgentDurableState` by removing the `sealed` keyword in `src/Core/Contracts/AgentDurableState.cs`.
3. **EnrichWithContext is called inside StreamResponseCore:** The private `StreamResponseCore(string, ct)` method calls `EnrichWithContext` internally (Agent.cs:86). The new `GetResponseStream(ChatMessage)` overload must NOT call `EnrichWithContext` separately — it should extract `.Text`, delegate to `StreamResponseCore`, which handles enrichment. For multimodal parts (images, files), pass them via the session or a grain-scoped field that `StreamResponseCore` can access during the M.E.AI message construction.
4. **Attribute naming:** The LLM injection attribute is `[LlmAttribute<T>]` (not `[Llm<T>]`). The embedding equivalent should be `[EmbeddingAttribute<T>]`.
5. **Primary constructor pattern:** `AgentDurableState` uses a primary constructor. `ProjectDurableState` should use the same pattern for consistency, not `required` init properties.

---

## File Structure

### New Files (by slice)

**Slice 1 — Core + Project + Routing:**
```
src/Core/Contracts/ContentPart.cs              -- ContentPart, TextContent, ImageContent, FileContent records
src/Core/Contracts/IProject.cs                 -- IProject grain interface (extends IAgent)
src/Core/Contracts/IUserProfile.cs             -- IUserProfile grain interface
src/Core/Contracts/ProjectDurableState.cs       -- extends AgentDurableState with project collections
src/Core/Contracts/UserProfileDurableState.cs   -- durable state for UserProfile grain
src/Core/Contracts/ProjectInfo.cs              -- lightweight project metadata record
src/Core/Contracts/FileReference.cs            -- file metadata record for project files dict
src/Core/Agents/ProjectStateAttribute.cs       -- [ProjectState] attribute + mapper
src/Core/Agents/UserProfileStateAttribute.cs   -- [UserProfileState] attribute + mapper
src/Agents/Projects/Project.cs                 -- Project : Agent grain implementation
src/Agents/UserProfile/UserProfile.cs          -- UserProfile : DurableGrain implementation
test/Core.Tests/ProjectTests.cs                -- AgentTest<Project> tests
test/Core.Tests/UserProfileTests.cs            -- UserProfile grain tests
test/Core.Tests/ChatMessageMultimodalTests.cs  -- ChatMessage serialization + Text property tests
```

**Slice 2 — UISession + Approvals:**
```
src/Core/Contracts/IUISession.cs               -- IUISession grain interface
src/Core/Contracts/UISessionDurableState.cs    -- durable state for UISession
src/Core/Contracts/UI/WidgetState.cs           -- abstract WidgetState + ButtonGridState
src/Core/Contracts/UI/Button.cs                -- Button, ButtonRow records
src/Core/Contracts/UI/ApprovalTypes.cs         -- PendingApproval, ApprovalResult, CallbackResult
src/Core/Agents/UISessionStateAttribute.cs     -- [UISessionState] attribute + mapper
src/Agents/UI/UISession.cs                     -- UISession : DurableGrain implementation
test/Core.Tests/UISessionTests.cs              -- UISession approval + callback tests
```

**Slice 3 — File Storage + RAG + Embeddings:**
```
src/Core/AI/EmbeddingModel.cs                  -- abstract base class (mirrors LLMModel)
src/Core/AI/Models/MxbaiEmbedLarge.cs          -- embedding model singleton
src/Core/AI/Models/TextEmbedding3Small.cs      -- embedding model singleton
src/Core/AI/EmbeddingAttribute.cs              -- [Embedding<T>] attribute + mapper
src/Core/Ingestion/IIngestionSource.cs         -- ingestion source interface
src/Core/Ingestion/IngestedChunk.cs            -- Qdrant vector record
src/Core/Ingestion/IngestedDocument.cs         -- document metadata record
src/Core/Ingestion/PdfIngestionSource.cs       -- PdfPig-based PDF ingestion
src/Core/Ingestion/DocumentIngestor.cs         -- ingestion pipeline orchestrator
src/Core/Context/RAGContextProvider.cs         -- Qdrant search context provider
src/Core/Services/BlobFileStorage.cs           -- Azure Blob upload/download wrapper
src/Hosting/IAWEmbeddingExtensions.cs          -- WithEmbedding<T>() + WithFileStorage() + WithVectorStore()
test/Core.Tests/EmbeddingModelTests.cs         -- EmbeddingModel registry tests
test/Core.Tests/Ingestion/PdfIngestionTests.cs -- PDF chunking tests
```

**Slice 4 — Dashboard + Tasks + Schedules:**
```
src/Core/Contracts/ProjectTask.cs              -- ProjectTask record + enums
src/Core/Contracts/ScheduledJob.cs             -- ScheduledJob record
src/Core/Contracts/ProjectDashboard.cs         -- dashboard data record
src/Core/Contracts/Events/DashboardChangedEvent.cs -- Orleans stream event
test/Core.Tests/DashboardRenderTests.cs        -- dashboard MarkdownV2 rendering tests
test/Core.Tests/ProjectTaskTests.cs            -- task lifecycle tests
```

**Slice 5 — Context Providers + Chat Reducers:**
```
src/Core/Context/UserContextProvider.cs        -- queries UserProfile grain
src/Core/Context/ProjectContextProvider.cs     -- project meta, file inventory
src/Core/Context/TaskContextProvider.cs        -- active tasks, approvals
src/Core/Agents/ChatReducer.cs                 -- 3-tier chat reduction logic
src/Core/Agents/HistorySummarizer.cs           -- LLM-based summarization trigger at 40 messages
test/Core.Tests/Context/UserContextProviderTests.cs
test/Core.Tests/Context/ProjectContextProviderTests.cs
test/Core.Tests/Context/TaskContextProviderTests.cs
test/Core.Tests/ChatReducerTests.cs
```

**Slice 6 — Full Dynamic UI:**
```
src/Core/Contracts/UI/WizardState.cs           -- wizard types
src/Core/Contracts/UI/PaginatorState.cs        -- paginator types
src/Core/Contracts/UI/MenuState.cs             -- menu types
src/Core/Contracts/UI/FormState.cs             -- form types
test/Core.Tests/UI/WizardTests.cs
test/Core.Tests/UI/PaginatorTests.cs
test/Core.Tests/UI/MenuTests.cs
test/Core.Tests/UI/FormTests.cs
```

### Modified Files (key ones)

```
src/Core/Contracts/ChatMessage.cs              -- add Parts [Id(3)], Text property
src/Core/Contracts/IAgent.cs                   -- add GetResponseStream(ChatMessage) overload
src/Core/Agents/Agent.cs                       -- implement ChatMessage overload, update EnrichWithContext
src/Core/Agents/DurableChatHistoryProvider.cs  -- dual-path: Parts vs Content fallback
src/Clients.Telegram/Program.cs               -- updated DI, routing
src/Clients.Telegram/TelegramBotService.cs     -- topic routing, file upload, multimodal, callbacks
src/Clients.Telegram/StreamSubscriber.cs       -- dashboard events, approval events, debounce
src/Clients.Telegram/Telegram.csproj           -- new package refs (Azure.Storage.Blobs, etc)
src/IAW.AppHost/AppHost.cs                     -- Azurite, Qdrant, WithEmbedding, WithFileStorage
Directory.Packages.props                       -- new package versions
```

---

## Chunk 1: Slice 1 — Multimodal Chat + Project Grain + Topic Routing

### Task 1: Extend ChatMessage with ContentPart Types

**Files:**
- Create: `src/Core/Contracts/ContentPart.cs`
- Modify: `src/Core/Contracts/ChatMessage.cs`
- Test: `test/Core.Tests/ChatMessageMultimodalTests.cs`

- [ ] **Step 1: Write failing tests for multimodal ChatMessage**

```csharp
// test/Core.Tests/ChatMessageMultimodalTests.cs
using Core.Contracts;

namespace IAW.Core.Tests;

public class ChatMessageMultimodalTests
{
    [Fact]
    public void Text_WithParts_ReturnsTextContent()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Parts = [new TextContent("hello"), new TextContent(" world")]
        };
        Assert.Equal("hello world", msg.Text);
    }

    [Fact]
    public void Text_WithContentOnly_FallsBack()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Content = "legacy text"
        };
        Assert.Equal("legacy text", msg.Text);
    }

    [Fact]
    public void Text_WithPartsAndContent_PrefersPartsResult()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Content = "old",
            Parts = [new TextContent("new")]
        };
        Assert.Equal("new", msg.Text);
    }

    [Fact]
    public void Text_WithEmptyParts_FallsBackToContent()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Content = "fallback",
            Parts = []
        };
        Assert.Equal("fallback", msg.Text);
    }

    [Fact]
    public void Text_WithMixedParts_ReturnsOnlyText()
    {
        var msg = new ChatMessage
        {
            Role = "user",
            Parts =
            [
                new TextContent("describe this: "),
                new ImageContent("blob://img.jpg", "image/jpeg", "photo"),
                new TextContent(" please")
            ]
        };
        Assert.Equal("describe this:  please", msg.Text);
    }

    [Fact]
    public void FileContent_StoresMetadata()
    {
        var file = new FileContent("blob://doc.pdf", "doc.pdf", "application/pdf", 245000, false);
        Assert.Equal("doc.pdf", file.FileName);
        Assert.False(file.Ingested);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~ChatMessageMultimodal" -v m`
Expected: compilation errors (ContentPart, TextContent, ImageContent, FileContent, Parts not defined)

- [ ] **Step 3: Create ContentPart types**

```csharp
// src/Core/Contracts/ContentPart.cs
using System.Text.Json.Serialization;

namespace Core.Contracts;

[GenerateSerializer]
[JsonDerivedType(typeof(TextContent))]
[JsonDerivedType(typeof(ImageContent))]
[JsonDerivedType(typeof(FileContent))]
public abstract record ContentPart;

[GenerateSerializer]
public sealed record TextContent([Id(0)] string Text) : ContentPart;

[GenerateSerializer]
public sealed record ImageContent(
    [Id(0)] string BlobUri,
    [Id(1)] string MimeType,
    [Id(2)] string? Caption) : ContentPart;

[GenerateSerializer]
public sealed record FileContent(
    [Id(0)] string BlobUri,
    [Id(1)] string FileName,
    [Id(2)] string MimeType,
    [Id(3)] long SizeBytes,
    [Id(4)] bool Ingested) : ContentPart;
```

- [ ] **Step 4: Update ChatMessage with Parts field**

Modify `src/Core/Contracts/ChatMessage.cs`. Add `[Id(3)] Parts` field and `Text` computed property. Keep `Content` at `[Id(1)]` for backward compat.

```csharp
[GenerateSerializer]
public sealed record ChatMessage
{
    [Id(0)] public string Role { get; init; } = string.Empty;
    [Id(1)] public string Content { get; init; } = string.Empty;
    [Id(2)] public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    [Id(3)] public IReadOnlyList<ContentPart> Parts { get; init; } = [];

    public string Text => Parts.Count > 0
        ? string.Join("", Parts.OfType<TextContent>().Select(p => p.Text))
        : Content ?? string.Empty;
}
```

- [ ] **Step 5: Fix all compilation errors in codebase**

Search for all usages of `ChatMessage.Content` and update to `ChatMessage.Text` where they read the message. Search for places constructing ChatMessage — update to use `Parts` for new code, but also set `Content = Text` for backward compat.

Run: `dotnet build IAW.slnx` — fix until green.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~ChatMessageMultimodal" -v m`
Expected: all 6 tests PASS

- [ ] **Step 7: Run full test suite to verify no regressions**

Run: `dotnet test IAW.slnx -v m`
Expected: all existing tests still PASS

- [ ] **Step 8: Commit**

```bash
git add src/Core/Contracts/ContentPart.cs src/Core/Contracts/ChatMessage.cs test/Core.Tests/ChatMessageMultimodalTests.cs
git commit -m "feat: extend ChatMessage with multimodal ContentPart types"
```

---

### Task 2: Add GetResponseStream(ChatMessage) Overload to IAgent

**Files:**
- Modify: `src/Core/Contracts/IAgent.cs` (line 8)
- Modify: `src/Core/Agents/Agent.cs` (lines 62-68, 184-211)

- [ ] **Step 1: Add overload to IAgent interface**

In `src/Core/Contracts/IAgent.cs`, add after line 8:

```csharp
IAsyncEnumerable<string> GetResponseStream(ChatMessage message, CancellationToken ct);
```

Note: no `= default` — match existing IAgent style.

- [ ] **Step 2: Implement in Agent base class**

**Important:** `StreamResponseCore` (Agent.cs:86) already calls `EnrichWithContext` internally. The new overload must NOT call `EnrichWithContext` — just pass `message.Text` to `StreamResponseCore` which handles enrichment. Store the multimodal parts in a grain-scoped field so `DurableChatHistoryProvider` can access them during M.E.AI conversion.

```csharp
// field on Agent to hold current multimodal parts for DurableChatHistoryProvider
private IReadOnlyList<ContentPart>? _currentMessageParts;

public IAsyncEnumerable<string> GetResponseStream(
    ChatMessage message, CancellationToken ct)
{
    // store the multimodal message as-is in history
    History.Add(message);

    // stash multimodal parts for DurableChatHistoryProvider to access
    _currentMessageParts = message.Parts;

    // delegate to existing pipeline — StreamResponseCore calls EnrichWithContext internally
    return StreamResponseCore(message.Text, ct);
}
```

Make the existing `GetResponseStream(string prompt, ...)` wrap the string into a ChatMessage:

```csharp
public IAsyncEnumerable<string> GetResponseStream(
    string prompt, CancellationToken ct)
{
    var message = new ChatMessage
    {
        Role = "user",
        Content = prompt,
        Parts = [new TextContent(prompt)]
    };
    return GetResponseStream(message, ct);
}
```

Note: the existing `GetResponseStream(string)` now delegates to the ChatMessage overload, which does NOT call `EnrichWithContext` — it delegates to `StreamResponseCore` which calls it. This preserves the existing single-enrichment behavior.

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build IAW.slnx`
Expected: green build

- [ ] **Step 4: Run full tests**

Run: `dotnet test IAW.slnx -v m`
Expected: all tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/Contracts/IAgent.cs src/Core/Agents/Agent.cs
git commit -m "feat: add GetResponseStream(ChatMessage) overload for multimodal input"
```

---

### Task 3: Update DurableChatHistoryProvider for Multimodal

**Files:**
- Modify: `src/Core/Agents/DurableChatHistoryProvider.cs`

- [ ] **Step 1: Update ProvideChatHistoryAsync to handle Parts**

In `src/Core/Agents/DurableChatHistoryProvider.cs`, update the conversion logic. If `Parts` is non-empty, build M.E.AI ChatMessage with appropriate content types. If empty, fall back to `Content` string.

```csharp
// In ProvideChatHistoryAsync, replace the existing conversion:
foreach (var msg in messages)
{
    var role = msg.Role == "user" ? ChatRole.User : ChatRole.Assistant;

    if (msg.Parts.Count > 0)
    {
        var contents = new List<AIContent>();
        foreach (var part in msg.Parts)
        {
            switch (part)
            {
                case TextContent tc:
                    contents.Add(new Microsoft.Extensions.AI.TextContent(tc.Text));
                    break;
                case ImageContent ic:
                    contents.Add(new Microsoft.Extensions.AI.TextContent(
                        $"[Image: {ic.Caption ?? ic.MimeType}]"));
                    break;
                case FileContent fc:
                    contents.Add(new Microsoft.Extensions.AI.TextContent(
                        $"[File: {fc.FileName}{(fc.Ingested ? " (indexed)" : "")}]"));
                    break;
            }
        }
        history.Add(new Microsoft.Extensions.AI.ChatMessage(role, contents));
    }
    else
    {
        history.Add(new Microsoft.Extensions.AI.ChatMessage(role, msg.Content ?? string.Empty));
    }
}
```

- [ ] **Step 2: Update StoreChatHistoryAsync for multimodal**

When storing response messages, create ChatMessage with Parts:

```csharp
// store response with Parts
_history.Add(new Contracts.ChatMessage
{
    Role = "assistant",
    Content = response.Text ?? string.Empty,
    Parts = [new TextContent(response.Text ?? string.Empty)]
});
```

- [ ] **Step 3: Build and test**

Run: `dotnet build IAW.slnx && dotnet test IAW.slnx -v m`
Expected: green build, all tests PASS

- [ ] **Step 4: Commit**

```bash
git add src/Core/Agents/DurableChatHistoryProvider.cs
git commit -m "feat: update DurableChatHistoryProvider for multimodal ContentPart handling"
```

---

### Task 4: Build UserProfile Grain

**Files:**
- Create: `src/Core/Contracts/IUserProfile.cs`
- Create: `src/Core/Contracts/UserProfileDurableState.cs`
- Create: `src/Core/Contracts/ProjectInfo.cs`
- Create: `src/Core/Agents/UserProfileStateAttribute.cs`
- Create: `src/Agents/UserProfile/UserProfile.cs`
- Test: `test/Core.Tests/UserProfileTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// test/Core.Tests/UserProfileTests.cs
namespace IAW.Core.Tests;

public class UserProfileTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder();
        // configure with journaling + volatile storage
        builder.AddSiloBuilderConfigurator<UserProfileTestSiloConfig>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    private IUserProfile Profile(string id) => _cluster.Client.GetGrain<IUserProfile>(id);

    [Fact]
    public async Task RegisterProject_And_ResolveProject_RoundTrips()
    {
        var profile = Profile("test-user-1");
        await profile.RegisterProject("my-app", "topic-42", default);

        var slug = await profile.ResolveProject("topic-42", default);
        Assert.Equal("my-app", slug);
    }

    [Fact]
    public async Task GetProjects_ReturnsRegisteredProjects()
    {
        var profile = Profile("test-user-2");
        await profile.RegisterProject("alpha", "topic-1", default);
        await profile.RegisterProject("beta", "topic-2", default);

        var projects = await profile.GetProjects(default);
        Assert.Equal(2, projects.Count);
    }

    [Fact]
    public async Task RemoveProject_DeletesMapping()
    {
        var profile = Profile("test-user-3");
        await profile.RegisterProject("temp", "topic-99", default);
        await profile.RemoveProject("temp", default);

        var slug = await profile.ResolveProject("topic-99", default);
        Assert.Null(slug);
    }

    [Fact]
    public async Task SetPreference_And_GetPreferences_RoundTrips()
    {
        var profile = Profile("test-user-4");
        await profile.SetPreference("timezone", "Europe/Berlin", default);

        var prefs = await profile.GetPreferences(default);
        Assert.Equal("Europe/Berlin", prefs["timezone"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~UserProfileTests" -v m`
Expected: compilation errors

- [ ] **Step 3: Create IUserProfile interface**

```csharp
// src/Core/Contracts/IUserProfile.cs
namespace Core.Contracts;

[GrainType("user-profile-v1")]
public interface IUserProfile : IGrainWithStringKey
{
    Task<Dictionary<string, string>> GetPreferences(CancellationToken ct);
    Task SetPreference(string key, string value, CancellationToken ct);
    Task<IReadOnlyList<ProjectInfo>> GetProjects(CancellationToken ct);
    Task RegisterProject(string slug, string topicId, CancellationToken ct);
    Task RemoveProject(string slug, CancellationToken ct);
    Task<string?> ResolveProject(string topicId, CancellationToken ct);
    Task RememberFact(string fact, CancellationToken ct);
    Task<IReadOnlyList<string>> RecallFacts(string query, CancellationToken ct);
}
```

- [ ] **Step 4: Create supporting types**

```csharp
// src/Core/Contracts/ProjectInfo.cs
namespace Core.Contracts;

[GenerateSerializer]
public sealed record ProjectInfo(
    [Id(0)] string Slug,
    [Id(1)] string TopicId);
```

```csharp
// src/Core/Contracts/UserProfileDurableState.cs
namespace Core.Contracts;

public sealed class UserProfileDurableState
{
    public required IDurableDictionary<string, string> Preferences { get; init; }
    public required IDurableDictionary<string, string> Projects { get; init; }
}
```

- [ ] **Step 5: Create UserProfileState attribute + mapper**

Follow the existing `[AgentState]` / `AgentStateMapper` pattern in the codebase. Create `[UserProfileState]` attribute and `UserProfileStateMapper` that resolves `IDurableDictionary` instances from the journaling factory.

- [ ] **Step 6: Implement UserProfile grain**

```csharp
// src/Agents/UserProfile/UserProfile.cs
namespace IAW.Agents;

[GrainType("user-profile-v1")]
public class UserProfile(
    [UserProfileState] UserProfileDurableState state)
    : DurableGrain, IUserProfile
{
    public Task<Dictionary<string, string>> GetPreferences(CancellationToken ct)
    {
        var result = new Dictionary<string, string>();
        foreach (var entry in state.Preferences)
            result[entry.Key] = entry.Value;
        return Task.FromResult(result);
    }

    public Task SetPreference(string key, string value, CancellationToken ct)
    {
        state.Preferences[key] = value;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProjectInfo>> GetProjects(CancellationToken ct)
    {
        var result = state.Projects.Select(kv => new ProjectInfo(kv.Key, kv.Value)).ToList();
        return Task.FromResult<IReadOnlyList<ProjectInfo>>(result);
    }

    public Task RegisterProject(string slug, string topicId, CancellationToken ct)
    {
        state.Projects[slug] = topicId;
        return Task.CompletedTask;
    }

    public Task RemoveProject(string slug, CancellationToken ct)
    {
        state.Projects.Remove(slug);
        return Task.CompletedTask;
    }

    public Task<string?> ResolveProject(string topicId, CancellationToken ct)
    {
        var match = state.Projects.FirstOrDefault(kv => kv.Value == topicId);
        return Task.FromResult<string?>(match.Key);
    }

    public Task RememberFact(string fact, CancellationToken ct)
    {
        // placeholder — will be enhanced with embeddings in Slice 3
        state.Preferences[$"fact:{Guid.NewGuid():N}"[..16]] = fact;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> RecallFacts(string query, CancellationToken ct)
    {
        // placeholder — keyword match for now
        var facts = state.Preferences
            .Where(kv => kv.Key.StartsWith("fact:") &&
                         kv.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(facts);
    }
}
```

- [ ] **Step 7: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~UserProfileTests" -v m`
Expected: all 4 tests PASS

- [ ] **Step 8: Commit**

```bash
git add src/Core/Contracts/IUserProfile.cs src/Core/Contracts/ProjectInfo.cs \
  src/Core/Contracts/UserProfileDurableState.cs src/Core/Agents/UserProfileStateAttribute.cs \
  src/Agents/UserProfile/UserProfile.cs test/Core.Tests/UserProfileTests.cs
git commit -m "feat: add UserProfile grain with project registry and preferences"
```

---

### Task 5: Build Project Grain (Basic Chat)

**Files:**
- Create: `src/Core/Contracts/IProject.cs`
- Create: `src/Core/Contracts/ProjectDurableState.cs`
- Create: `src/Core/Contracts/FileReference.cs`
- Create: `src/Core/Agents/ProjectStateAttribute.cs`
- Create: `src/Agents/Projects/Project.cs`
- Test: `test/Core.Tests/ProjectTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// test/Core.Tests/ProjectTests.cs
using IAW.Testing;

namespace IAW.Core.Tests;

public class ProjectTests : AgentTest<Project>
{
    [Fact]
    public async Task GetResponse_WorksLikeStandardAgent()
    {
        var project = Agent(UniqueId("project"));
        var response = await project.GetResponse("hello", default);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetHistory_TracksConversation()
    {
        var project = Agent(UniqueId("project"));
        await project.GetResponse("test message", default);
        var history = await project.GetHistory(default);
        Assert.True(history.Count >= 2); // user + assistant
    }
}
```

- [ ] **Step 2: Create IProject interface**

```csharp
// src/Core/Contracts/IProject.cs
namespace Core.Contracts;

[GrainType("project-v1")]
public interface IProject : IAgent
{
    Task<ProjectDashboard> GetDashboard(CancellationToken ct);
    Task<ProjectTask> AddTask(string description, TaskPriority priority, CancellationToken ct);
    Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct);
    Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct);
    Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct);
    Task CancelJob(string jobId, CancellationToken ct);
    Task RegisterFile(FileReference fileRef, CancellationToken ct);
    Task RequestApproval(string question, string[] options, CancellationToken ct);
    Task<ProjectContext> GetProjectContext(CancellationToken ct);
}
```

Note: `ProjectDashboard`, `ProjectTask`, `ProjectContext`, `ScheduledJob` types will be stubs in Slice 1, implemented fully in Slice 4.

- [ ] **Step 3: Create ProjectDurableState**

```csharp
// src/Core/Contracts/ProjectDurableState.cs
namespace Core.Contracts;

// Primary constructor pattern matching AgentDurableState — must call base constructor
public class ProjectDurableState(
    IDurableDictionary<string, StateEntry> state,
    IDurableList<AgentEvent> eventLog,
    IDurableList<ChatMessage> history,
    IDurableDictionary<string, TrackingItem> trackingItems,
    IDurableList<ProjectTask> tasks,
    IDurableDictionary<string, ScheduledJob> schedules,
    IDurableDictionary<string, FileReference> files,
    IDurableDictionary<string, string> projectMeta)
    : AgentDurableState(state, eventLog, history, trackingItems)
{
    public IDurableList<ProjectTask> Tasks => tasks;
    public IDurableDictionary<string, ScheduledJob> Schedules => schedules;
    public IDurableDictionary<string, FileReference> Files => files;
    public IDurableDictionary<string, string> ProjectMeta => projectMeta;
}
```

Note: `AgentDurableState` must be unsealed first (see Codebase Notes #2).

- [ ] **Step 4: Create FileReference and stub types**

```csharp
// src/Core/Contracts/FileReference.cs
namespace Core.Contracts;

[GenerateSerializer]
public sealed record FileReference(
    [Id(0)] string BlobUri,
    [Id(1)] string FileName,
    [Id(2)] string MimeType,
    [Id(3)] long SizeBytes,
    [Id(4)] bool Ingested,
    [Id(5)] DateTimeOffset UploadedAt);
```

Create stub records for `ProjectDashboard`, `ProjectContext` (will be fleshed out in Slice 4).

- [ ] **Step 5: Create [ProjectState] attribute + mapper**

Mirror the `[AgentState]` pattern but resolve both the base `AgentDurableState` collections AND the project-specific ones.

- [ ] **Step 6: Implement Project grain**

```csharp
// src/Agents/Projects/Project.cs
namespace IAW.Agents.Projects;

[GrainType("project-v1")]
public class Project(
    [ProjectState] ProjectDurableState durableState,
    [LlmAttribute<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient), IProject
{
    protected override string Instructions => """
        You are a project assistant. Help the user manage their project,
        answer questions, and coordinate tasks.
        Be concise and actionable in your responses.
        """;

    protected override string DisplayName => "Project";

    // Slice 1: stub implementations, fleshed out in Slice 4
    public Task<ProjectDashboard> GetDashboard(CancellationToken ct) =>
        Task.FromResult(new ProjectDashboard());

    public Task<ProjectTask> AddTask(string description, TaskPriority priority, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task UpdateTask(string taskId, ProjectTaskStatus status, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task<IReadOnlyList<ProjectTask>> GetTasks(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ProjectTask>>([]);

    public Task<ScheduledJob> ScheduleJob(string name, TimeSpan interval, string description, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task CancelJob(string jobId, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 4");

    public Task RegisterFile(FileReference fileRef, CancellationToken ct) =>
        Task.CompletedTask; // stub — records file in state, fleshed out in Slice 3

    public Task RequestApproval(string question, string[] options, CancellationToken ct) =>
        throw new NotImplementedException("Implemented in Slice 2");

    public Task<ProjectContext> GetProjectContext(CancellationToken ct) =>
        Task.FromResult(new ProjectContext());
}
```

- [ ] **Step 7: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~ProjectTests" -v m`
Expected: PASS (inherits AgentTest<T> universal tests + custom tests)

- [ ] **Step 8: Run full suite**

Run: `dotnet test IAW.slnx -v m`
Expected: all tests PASS

- [ ] **Step 9: Commit**

```bash
git add src/Core/Contracts/IProject.cs src/Core/Contracts/ProjectDurableState.cs \
  src/Core/Contracts/FileReference.cs src/Core/Agents/ProjectStateAttribute.cs \
  src/Agents/Projects/Project.cs test/Core.Tests/ProjectTests.cs
git commit -m "feat: add Project grain with basic chat capability"
```

---

### Task 6: Refactor TelegramBotService for Topic-Based Routing

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`
- Modify: `src/Clients.Telegram/Program.cs`

- [ ] **Step 1: Update TelegramBotService to resolve projects from forum topics**

Replace the direct `IPersonalAssistant` grain reference with a routing flow:
1. Extract `telegramId` from `Update.Message.From.Id`
2. Extract `topicId` from `Update.Message.MessageThreadId` (null for main chat)
3. Call `UserProfile.ResolveProject(topicId)` to get project slug
4. If no project exists for this topic, auto-create one via `UserProfile.RegisterProject()`
5. Get `IProject` grain via `clusterClient.GetGrain<IProject>("{telegramId}/{projectSlug}")`
6. Call `project.GetResponseStream(chatMessage)` instead of the PA

- [ ] **Step 2: Build multimodal ChatMessage from Telegram Update**

Add a method that constructs `ChatMessage` from the Telegram `Update`:
- Text messages: `[new TextContent(text)]`
- Voice messages: transcribe first, then `[new TextContent(transcription)]`
- Keep existing voice transcription logic

- [ ] **Step 3: Update Program.cs DI**

Remove direct `IPersonalAssistant` references. The `TelegramBotService` now takes `IClusterClient` and resolves grains dynamically.

- [ ] **Step 4: Manual test with aspire run**

Run: `aspire run`
Expected: Telegram bot starts, can chat in a forum topic, messages route to project grains

- [ ] **Step 5: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs src/Clients.Telegram/Program.cs
git commit -m "feat: refactor TelegramBotService for project-based forum topic routing"
```

---

### Task 7: Integration Test — End-to-End Topic Routing

- [ ] **Step 1: Run aspire build + test**

Run: `dotnet build IAW.slnx && dotnet test IAW.slnx -v m`
Expected: all tests pass, no regressions

- [ ] **Step 2: Run aspire and verify manually**

Run: `aspire run`
Verify:
- Bot responds in forum topics
- Different topics create different project grains
- History is isolated per topic

- [ ] **Step 3: Commit any fixes**

```bash
git commit -m "fix: resolve integration issues in topic routing"
```

---

## Chunk 2: Slice 2 — UISession + Inline Keyboards + Approvals

### Task 8: Build UISession Grain with Approval Support

**Files:**
- Create: `src/Core/Contracts/IUISession.cs`
- Create: `src/Core/Contracts/UISessionDurableState.cs`
- Create: `src/Core/Contracts/UI/WidgetState.cs`
- Create: `src/Core/Contracts/UI/Button.cs`
- Create: `src/Core/Contracts/UI/ApprovalTypes.cs`
- Create: `src/Core/Agents/UISessionStateAttribute.cs`
- Create: `src/Agents/UI/UISession.cs`
- Test: `test/Core.Tests/UISessionTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// test/Core.Tests/UISessionTests.cs
namespace IAW.Core.Tests;

public class UISessionTests : IAsyncLifetime
{
    private TestCluster _cluster = null!;

    public async Task InitializeAsync() { /* setup cluster */ }
    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    private IUISession Session(string id) => _cluster.Client.GetGrain<IUISession>(id);

    [Fact]
    public async Task RegisterApproval_And_ResolveApproval_RoundTrips()
    {
        var session = Session("user-1");
        await session.RegisterApproval("ap1", "Deploy to prod?", ["yes", "no"], "my-project", default);
        var result = await session.ResolveApproval("ap1", "yes", default);
        Assert.Equal("ap1", result.ApprovalId);
        Assert.Equal("yes", result.Decision);
    }

    [Fact]
    public async Task HandleCallback_RoutesApproval()
    {
        var session = Session("user-2");
        await session.RegisterApproval("ap2", "Merge PR?", ["approve", "decline"], "proj", default);
        var result = await session.HandleCallback("ap2", "ap:ap2:approve", default);
        Assert.Equal("approve", result.Action);
    }

    [Fact]
    public async Task HasPendingFreeTextInput_ReturnsFalseByDefault()
    {
        var session = Session("user-3");
        var pending = await session.HasPendingFreeTextInput("topic-1", default);
        Assert.False(pending);
    }
}
```

- [ ] **Step 2: Create UI contract types**

```csharp
// src/Core/Contracts/UI/ApprovalTypes.cs
namespace Core.Contracts.UI;

[GenerateSerializer]
public sealed record PendingApproval(
    [Id(0)] string Id,
    [Id(1)] string Question,
    [Id(2)] IReadOnlyList<string> Options,
    [Id(3)] string ProjectSlug,
    [Id(4)] int MessageId,
    [Id(5)] DateTimeOffset CreatedAt);

[GenerateSerializer]
public sealed record ApprovalResult(
    [Id(0)] string ApprovalId,
    [Id(1)] string Decision,
    [Id(2)] string ProjectSlug);

[GenerateSerializer]
public sealed record CallbackResult(
    [Id(0)] string? NewText,
    [Id(1)] string? Action,
    [Id(2)] string? Toast);
```

Create `WidgetState.cs` and `Button.cs` with the records from the spec (Section 6.1, 6.2). Use `[Id(0-9)]` for base WidgetState, `[Id(10+)]` for derived.

- [ ] **Step 3: Create IUISession interface**

```csharp
// src/Core/Contracts/IUISession.cs
namespace Core.Contracts;

[GrainType("ui-session-v1")]
public interface IUISession : IGrainWithStringKey
{
    Task<CallbackResult> HandleCallback(string callbackId, string callbackData, CancellationToken ct);
    Task RegisterApproval(string approvalId, string question, string[] options, string projectSlug, CancellationToken ct);
    Task<ApprovalResult> ResolveApproval(string approvalId, string decision, CancellationToken ct);
    Task<bool> HasPendingFreeTextInput(string topicId, CancellationToken ct);
}
```

- [ ] **Step 4: Create UISessionDurableState, attribute, mapper**

```csharp
// src/Core/Contracts/UISessionDurableState.cs
namespace Core.Contracts;

public sealed class UISessionDurableState(
    IDurableDictionary<string, PendingApproval> pendingApprovals)
{
    public IDurableDictionary<string, PendingApproval> PendingApprovals { get; } = pendingApprovals;
    // activeWidgets and activeWizards deferred to Slice 6
}
```

Create `[UISessionState]` attribute + `UISessionStateMapper` following the `[AgentState]`/`AgentStateMapper` pattern in `src/Core/AI/AgentStateMapper.cs`. The mapper resolves `IDurableDictionary<string, PendingApproval>` via keyed DI from the journaling factory.

- [ ] **Step 5: Implement UISession grain**

Parse callback data format `"{type}:{id}:{action}"` and route accordingly. For now, only implement approval routing (`"ap:"` prefix).

- [ ] **Step 6: Run tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~UISessionTests" -v m`
Expected: all 3 tests PASS

- [ ] **Step 7: Commit**

```bash
git add src/Core/Contracts/IUISession.cs src/Core/Contracts/UI/ src/Agents/UI/UISession.cs \
  test/Core.Tests/UISessionTests.cs
git commit -m "feat: add UISession grain with approval flow and callback routing"
```

---

### Task 9: Add RequestApproval Tool to Project Grain

**Files:**
- Modify: `src/Agents/Projects/Project.cs`

- [ ] **Step 1: Add RequestApproval as an LLM tool**

In `Project.cs`, override `DefineTools()` and add a `RequestApproval` tool that publishes an `approval.requested` event to the Orleans stream:

```csharp
[Description("Ask user to approve or decline something")]
async Task<string> RequestApproval(
    [Description("The question to ask")] string question,
    [Description("Available options")] string[] options)
{
    var approvalId = Guid.NewGuid().ToString("N")[..8];
    await PublishAsync("approval.requested", new Dictionary<string, object>
    {
        ["approvalId"] = approvalId,
        ["question"] = question,
        ["options"] = options,
        ["projectSlug"] = this.GetPrimaryKeyString()
    }, default);
    return $"Approval requested (id: {approvalId}). Waiting for user response.";
}
```

- [ ] **Step 2: Build and test**

Run: `dotnet build IAW.slnx && dotnet test IAW.slnx -v m`

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Projects/Project.cs
git commit -m "feat: add RequestApproval tool to Project grain"
```

---

### Task 10: Wire Callback Routing in TelegramBotService

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`
- Modify: `src/Clients.Telegram/StreamSubscriber.cs`

- [ ] **Step 1: Handle CallbackQuery in TelegramBotService**

Add callback query handling to `HandleUpdateCoreAsync`:
1. Check `update.CallbackQuery` is not null
2. Parse `callbackQuery.Data` as `"{type}:{id}:{action}"`
3. Get `IUISession` grain for the user
4. Call `session.HandleCallback(callbackQuery.Id, callbackQuery.Data, ct)`
5. Call `answerCallbackQuery` to clear loading indicator
6. Edit message text/markup based on result

- [ ] **Step 2: Handle approval.requested in StreamSubscriber**

Subscribe to `"approval.requested"` stream. When received:
1. Parse payload (question, options, approvalId, projectSlug)
2. Get `IUISession` grain, call `RegisterApproval()`
3. Build `InlineKeyboardMarkup` with buttons for each option
4. Send message with inline keyboard to the appropriate topic

- [ ] **Step 3: Implement message routing priority**

In `HandleUpdateCoreAsync`, before routing to `Project.GetResponseStream()`:
1. Check `UISession.HasPendingFreeTextInput(topicId)` — if true, route to UISession
2. Check if message is a reply to an approval message — if true, route as clarification
3. Otherwise, normal routing to Project

- [ ] **Step 4: Manual test**

Run: `aspire run`
Test: trigger an approval via the bot, verify inline buttons appear, verify tapping works

- [ ] **Step 5: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs src/Clients.Telegram/StreamSubscriber.cs
git commit -m "feat: wire approval flow with inline keyboards in Telegram client"
```

---

## Chunk 3: Slice 3 — File Storage + Qdrant RAG + Embedding Infrastructure

### Task 11: Build EmbeddingModel Base Class

**Files:**
- Create: `src/Core/AI/EmbeddingModel.cs`
- Create: `src/Core/AI/Models/MxbaiEmbedLarge.cs`
- Create: `src/Core/AI/Models/TextEmbedding3Small.cs`
- Create: `src/Core/AI/EmbeddingAttribute.cs`
- Test: `test/Core.Tests/EmbeddingModelTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// test/Core.Tests/EmbeddingModelTests.cs
namespace IAW.Core.Tests;

public class EmbeddingModelTests
{
    [Fact]
    public void MxbaiEmbedLarge_RegistersInRegistry()
    {
        EmbeddingModel.EnsureAllModelsLoaded();
        var model = EmbeddingModel.All.FirstOrDefault(m => m.Id == "mxbai-embed-large");
        Assert.NotNull(model);
        Assert.Equal(1024, model.Dimensions);
        Assert.Equal("ollama", model.Provider);
    }

    [Fact]
    public void TextEmbedding3Small_RegistersInRegistry()
    {
        EmbeddingModel.EnsureAllModelsLoaded();
        var model = EmbeddingModel.All.FirstOrDefault(m => m.Id == "text-embedding-3-small");
        Assert.NotNull(model);
        Assert.Equal(1536, model.Dimensions);
        Assert.Equal("openai", model.Provider);
    }

    [Fact]
    public void ServiceKey_MatchesLLMModelFormula()
    {
        EmbeddingModel.EnsureAllModelsLoaded();
        var model = EmbeddingModel.All.First(m => m.Id == "text-embedding-3-small");
        Assert.Equal("openai-text-embedding-3-small", model.ServiceKey);
    }
}
```

- [ ] **Step 2: Implement EmbeddingModel, models, attribute**

Mirror `LLMModel.cs` exactly for the base class. Create model singletons. Create `[Embedding<T>]` attribute with mapper resolving to keyed `IEmbeddingGenerator<string, Embedding<float>>`.

- [ ] **Step 3: Run tests, commit**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj --filter "FullyQualifiedName~EmbeddingModel" -v m`

```bash
git commit -m "feat: add EmbeddingModel base class with MxbaiEmbedLarge and TextEmbedding3Small"
```

---

### Task 12: Add Azurite + Qdrant to AppHost

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs`
- Modify: `src/IAW.AppHost/Aspire.csproj`
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add package references**

Add to `Directory.Packages.props`:
- `Aspire.Hosting.Azure.Storage`
- `Aspire.Hosting.Qdrant`

Use Context7 to look up latest versions before adding.

- [ ] **Step 2: Update AppHost.cs**

Add Azurite (with data volume) and Qdrant (with data volume) resources. Wire them into the Telegram client and silo projects via `WithReference()`.

```csharp
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(e => e.WithDataVolume("iaw-blobs"));
var blobs = storage.AddBlobs("file-storage");

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume("iaw-qdrant");
```

- [ ] **Step 3: Build hosting extensions**

Create `IAWEmbeddingExtensions.cs` in `src/Hosting/` with three extension methods:
- `WithEmbedding<T>()` — registers embedding model (mirrors `WithLLM<T>()`), injects `AI__Embedding__Models__*` env vars
- `WithFileStorage(blobs)` — passes blob connection to dependent projects
- `WithVectorStore(qdrant)` — passes Qdrant connection to dependent projects

Also add embedding provider registration in a new `EmbeddingRegistration.cs` (mirrors `LlmRegistration.cs`): reads `AI:Embedding:Models` config, registers keyed `IEmbeddingGenerator<string, Embedding<float>>` per model.

- [ ] **Step 4: Build and verify aspire run starts all containers**

Run: `aspire run`
Verify: Azurite and Qdrant containers start, dashboard shows them healthy

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: add Azurite blob storage and Qdrant to AppHost with data volumes"
```

---

### Task 13: Implement File Upload Flow

**Files:**
- Create: `src/Core/Services/BlobFileStorage.cs`
- Modify: `src/Clients.Telegram/TelegramBotService.cs`
- Modify: `src/Clients.Telegram/Telegram.csproj`

- [ ] **Step 1: Create BlobFileStorage service**

Wraps `BlobContainerClient` for upload/download:
- `UploadAsync(stream, path)` -> returns blob URI
- `DownloadAsync(blobUri)` -> returns stream
- Path format: `{telegramId}/{projectSlug}/{guid}-{filename}`

- [ ] **Step 2: Handle photo messages in TelegramBotService**

When `Update.Message.Photo` is not null:
1. Get highest-res photo (`PhotoSize` with largest `Width`)
2. Download via `getFile(fileId)`
3. Upload to blob storage
4. Build `ChatMessage` with `ImageContent` part
5. Send to `Project.GetResponseStream(chatMessage)`

- [ ] **Step 3: Handle document messages**

When `Update.Message.Document` is not null:
1. Download via `getFile(fileId)`
2. Upload to blob storage
3. Build `ChatMessage` with `FileContent` part
4. Send to Project grain
5. If `MimeType == "application/pdf"`, trigger ingestion (Task 14)

- [ ] **Step 4: Manual test**

Send a photo and a PDF to the bot. Verify they're stored in blob and the bot acknowledges them.

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: implement file upload flow from Telegram to Azure Blob"
```

---

### Task 14: Implement PDF Ingestion Pipeline

**Files:**
- Create: `src/Core/Ingestion/IIngestionSource.cs`
- Create: `src/Core/Ingestion/IngestedChunk.cs`
- Create: `src/Core/Ingestion/IngestedDocument.cs`
- Create: `src/Core/Ingestion/PdfIngestionSource.cs`
- Create: `src/Core/Ingestion/DocumentIngestor.cs`
- Modify: `src/Core/Core.csproj` (add PdfPig, SemanticKernel.Core packages)
- Test: `test/Core.Tests/Ingestion/PdfIngestionTests.cs`

- [ ] **Step 1: Add NuGet packages**

Add `UglyToad.PdfPig` and `Microsoft.SemanticKernel.Core` to `Directory.Packages.props`. Use Context7 to verify latest versions.

- [ ] **Step 2: Write tests for PDF chunking**

Test that PdfIngestionSource correctly extracts text and chunks it. Use a small test PDF or generate text programmatically.

- [ ] **Step 3: Implement IIngestionSource + PdfIngestionSource**

`PdfIngestionSource` opens a PDF stream with PdfPig, iterates pages using `DocstrumBoundingBoxes` for layout-aware text extraction, concatenates words into page text, then feeds each page's text through `TextChunker.SplitPlainTextParagraphs(text, maxTokensPerParagraph: 200)`. Returns `IReadOnlyList<IngestedChunk>` with page number and text per chunk.

- [ ] **Step 4: Implement DocumentIngestor**

Orchestrates: download blob -> extract chunks -> embed -> store in Qdrant. Takes `IEmbeddingGenerator` and Qdrant client.

- [ ] **Step 5: Run tests, commit**

```bash
git commit -m "feat: implement PDF ingestion pipeline with PdfPig and Qdrant"
```

---

### Task 15: Build RAGContextProvider

**Files:**
- Create: `src/Core/Context/RAGContextProvider.cs`
- Test: `test/Core.Tests/Context/RAGContextProviderTests.cs`

- [ ] **Step 1: Write tests**

Test that RAGContextProvider performs vector search and returns formatted results.

- [ ] **Step 2: Implement RAGContextProvider**

```csharp
public class RAGContextProvider(
    QdrantClient qdrantClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator) : IAgentContextProvider
{
    public string Name => "document-search";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct)
    {
        // agentId for Project grains is "{telegramId}/{projectSlug}"
        var collectionName = $"project-{agentId.Replace("/", "-")}";

        // check if collection exists (lazily created on first upload)
        if (!await CollectionExistsAsync(collectionName, ct)) return [];

        var queryEmbedding = await embeddingGenerator.GenerateEmbeddingAsync(prompt, cancellationToken: ct);
        var results = await qdrantClient.SearchAsync(
            collectionName, queryEmbedding.Vector.ToArray(), limit: 5, cancellationToken: ct);

        return results
            .Select(r => $"[document: {r.Payload["fileName"].StringValue}, page {r.Payload["pageNumber"].IntegerValue}] {r.Payload["text"].StringValue}")
            .ToList();
    }
}
```

- [ ] **Step 3: Wire into Project grain's GetContextProviders()**

Override `GetContextProviders()` in `Project.cs` to include `RAGContextProvider`. The provider gets its `QdrantClient` and `IEmbeddingGenerator` via DI (constructor injection on the Project grain or resolved from the grain's service provider).

- [ ] **Step 4: Run tests, commit**

```bash
git commit -m "feat: add RAGContextProvider for project-scoped document search"
```

---

### Task 15b: Image Handling with Vision Models

**Files:**
- Modify: `src/Core/Agents/DurableChatHistoryProvider.cs`

- [ ] **Step 1: Update DurableChatHistoryProvider for ImageContent**

In the dual-path conversion (already added in Task 3), extend the `Parts` path: when a `ContentPart` is `ImageContent`, convert it to M.E.AI `DataContent` by downloading the blob and passing the bytes with the MIME type. This enables vision-capable models (Sonnet 4.6) to see images. For non-vision models, fall back to `[Image: {caption}]` text placeholder.

- [ ] **Step 2: Manual test**

Send a photo to the bot via Telegram. Verify the LLM receives the image and can describe it.

- [ ] **Step 3: Commit**

```bash
git commit -m "feat: pass images to vision models via DurableChatHistoryProvider"
```

---

### Task 15c: Handle Telegram 4096-Character Limit

**Files:**
- Modify: `src/Clients.Telegram/TelegramBotService.cs`

- [ ] **Step 1: Add message splitting for streaming responses**

In the progressive-edit streaming loop: if accumulated text exceeds 4000 characters, stop editing the current message and send a new continuation message with "...(continued)" prefix. Track the chain of message IDs.

- [ ] **Step 2: Add message splitting for final responses**

When sending final (non-streaming) agent responses, split at sentence boundaries if exceeding 4096 chars. Send as multiple messages.

- [ ] **Step 3: Manual test**

Ask the bot to generate a long response (e.g., "explain Orleans grains in detail with examples"). Verify the response splits cleanly across messages.

- [ ] **Step 4: Commit**

```bash
git commit -m "feat: handle Telegram 4096-char message limit with auto-splitting"
```

---

## Chunk 4: Slice 4 — Dashboard + Task Board + Schedules

### Task 16: Implement ProjectTask and ScheduledJob Models

**Files:**
- Create: `src/Core/Contracts/ProjectTask.cs`
- Create: `src/Core/Contracts/ScheduledJob.cs`
- Create: `src/Core/Contracts/ProjectDashboard.cs`
- Create: `src/Core/Contracts/Events/DashboardChangedEvent.cs`

- [ ] **Step 1: Create all contract types from spec Sections 4.1-4.3**

Use the exact records from the spec with `[GenerateSerializer]` and `[Id(n)]` attributes. Also create:

```csharp
// src/Core/Contracts/Events/DashboardChangedEvent.cs
namespace Core.Contracts.Events;

[GenerateSerializer]
public sealed record DashboardChangedEvent(
    [Id(0)] string ProjectKey,
    [Id(1)] string RenderedMarkdown,
    [Id(2)] DateTimeOffset Timestamp);
```

- [ ] **Step 2: Build, commit**

```bash
git commit -m "feat: add ProjectTask, ScheduledJob, ProjectDashboard contracts"
```

---

### Task 17: Implement Task Management Tools in Project Grain

**Files:**
- Modify: `src/Agents/Projects/Project.cs`
- Test: `test/Core.Tests/ProjectTaskTests.cs`

- [ ] **Step 1: Write tests for AddTask, UpdateTask, GetTasks**

- [ ] **Step 2: Implement task management using durable state**

`AddTask` creates a `ProjectTask` in `durableState.Tasks`, `UpdateTask` finds and replaces, `GetTasks` returns filtered list.

- [ ] **Step 3: Add as LLM tools via DefineTools()**

- [ ] **Step 4: Run tests, commit**

```bash
git commit -m "feat: implement task management tools in Project grain"
```

---

### Task 18: Implement Dashboard Rendering and Pinned Message

**Files:**
- Modify: `src/Agents/Projects/Project.cs`
- Modify: `src/Clients.Telegram/StreamSubscriber.cs`
- Test: `test/Core.Tests/DashboardRenderTests.cs`

- [ ] **Step 1: Write tests for dashboard MarkdownV2 rendering**

Test that `BuildDashboard()` produces correct MarkdownV2 with task counts, active items, scheduled jobs, and respects 4096-char limit.

- [ ] **Step 2: Implement BuildDashboard() in Project grain**

Generate MarkdownV2 string from tasks, schedules, files. Publish `DashboardChangedEvent` on state changes.

- [ ] **Step 3: Handle DashboardChangedEvent in StreamSubscriber**

Subscribe to `"dashboard.changed"` stream. On event:
1. Get `pinnedMessageId` from payload
2. Call `editMessageText` with new dashboard content
3. Implement per-project debounce using `ConcurrentDictionary<string, Timer>` (2-second delay)

- [ ] **Step 4: Implement dashboard creation on new project**

When a project is created for the first time, send initial dashboard message and pin it.

- [ ] **Step 5: Run tests, manual test with aspire run, commit**

```bash
git commit -m "feat: implement live dashboard with pinned message and debounced updates"
```

---

### Task 19: Implement Scheduled Jobs via Orleans Reminders

**Files:**
- Modify: `src/Agents/Projects/Project.cs`

- [ ] **Step 1: Implement ScheduleJob and CancelJob**

Map scheduled jobs to Orleans reminders using the existing `StartTrackingAsync`/`StopTrackingAsync` pattern from `Agent.Tracking.cs`. When a reminder fires, feed the job's `Description` as a prompt to `GetResponseStream`.

- [ ] **Step 2: Test, commit**

```bash
git commit -m "feat: implement scheduled jobs via Orleans reminders in Project grain"
```

---

## Chunk 5: Slice 5 — Context Providers + Chat Reducers

### Task 20: Build Context Provider Chain

**Files:**
- Create: `src/Core/Context/UserContextProvider.cs`
- Create: `src/Core/Context/ProjectContextProvider.cs`
- Create: `src/Core/Context/TaskContextProvider.cs`
- Test: `test/Core.Tests/Context/UserContextProviderTests.cs`
- Test: `test/Core.Tests/Context/ProjectContextProviderTests.cs`

- [ ] **Step 1: Write tests for each provider**

Test that each provider returns correctly formatted context strings.

- [ ] **Step 2: Implement UserContextProvider**

Constructor takes `IGrainFactory`. In `GetContextAsync`, parse `agentId` to extract telegramId (`agentId.Split('/')[0]`), get `IUserProfile` grain, query preferences and facts, format as `[user] key: value`.

- [ ] **Step 3: Implement ProjectContextProvider**

Constructor takes `IGrainFactory`. In `GetContextAsync`, get `IProject` grain by `agentId` (which IS the project key `{telegramId}/{projectSlug}`), read project meta and file inventory, format as `[project] description, N tasks, N files`.

- [ ] **Step 4: Implement TaskContextProvider**

Constructor takes `IGrainFactory`. In `GetContextAsync`, get `IProject` grain by `agentId`, read active tasks, recent completions, pending approvals, format as structured text.

- [ ] **Step 5: Wire all providers into Project.GetContextProviders()**

Return all providers. Note: `RAGContextProvider` is from Chunk 3, `MemoryContextProvider` already exists at `src/Core/Context/MemoryContextProvider.cs`. Both are wired in here alongside the new providers.

Return: `[UserContextProvider, ProjectContextProvider, TaskContextProvider, RAGContextProvider, MemoryContextProvider]`

- [ ] **Step 6: Run tests, commit**

```bash
git commit -m "feat: add UserContext, ProjectContext, and TaskContext providers"
```

---

### Task 21: Implement Chat Reducer

**Files:**
- Create: `src/Core/Agents/ChatReducer.cs`
- Modify: `src/Core/Agents/DurableChatHistoryProvider.cs`
- Test: `test/Core.Tests/ChatReducerTests.cs`

- [ ] **Step 1: Write tests for 3-tier reduction**

Test that:
- Last message is always preserved
- Recent 20 messages are verbatim
- Older messages get summarized
- Non-reducible messages (with tool calls, approvals, file uploads) survive

- [ ] **Step 2: Implement ChatReducer**

```csharp
public class ChatReducer
{
    public IReadOnlyList<ChatMessage> Reduce(
        IReadOnlyList<ChatMessage> fullHistory,
        ChatMessage? summary,
        int recentWindow = 20)
    {
        // Tier 1: last message (always full)
        // Tier 2: recent window (last 20 verbatim)
        // Tier 3: summary block (if exists)
        // Non-reducible messages pinned into output
    }

    public bool IsNonReducible(ChatMessage message)
    {
        // check for tool calls, approvals, file uploads, "remember" keywords
    }
}
```

- [ ] **Step 3: Integrate into DurableChatHistoryProvider.ProvideChatHistoryAsync**

Inject `ChatReducer` into `DurableChatHistoryProvider` (currently takes `IDurableList<ChatMessage>` and `int maxMessages`; add `ChatReducer` as a parameter). Apply reduction before returning history to the LLM. The summary (if one exists) is stored as a synthetic system message at the start of the history list.

- [ ] **Step 4: Run tests, commit**

```bash
git commit -m "feat: implement 3-tier chat reducer with non-reducible message pinning"
```

---

### Task 21b: Implement Summarization Trigger

**Files:**
- Create: `src/Core/Agents/HistorySummarizer.cs`
- Modify: `src/Core/Agents/DurableChatHistoryProvider.cs`

- [ ] **Step 1: Implement HistorySummarizer**

Takes `IChatClient` for summarization. When history exceeds 40 messages:
1. Extract messages 21-40
2. Separate non-reducible messages (tool calls, approvals, file uploads)
3. Send remaining messages to LLM with prompt: "Summarize this conversation history, preserving key decisions, task assignments, and outcomes."
4. Create a synthetic `ChatMessage` (role: "system") with the summary
5. Store the summary in the durable list (replacing messages 21-40 with summary + non-reducible messages)

- [ ] **Step 2: Wire into DurableChatHistoryProvider**

Call `HistorySummarizer.SummarizeIfNeeded()` before `ChatReducer.Reduce()`. The summarizer modifies the durable list; the reducer produces the LLM context window.

- [ ] **Step 3: Test, commit**

```bash
git commit -m "feat: add history summarization trigger at 40 messages"
```

---

### Task 21c: Image Eviction from Context Window

**Files:**
- Modify: `src/Core/Agents/ChatReducer.cs`

- [ ] **Step 1: Add image eviction to ChatReducer**

When a message with `ImageContent` parts leaves the recent window (Tier 2) and enters the summarized tier (Tier 3):
1. Before summarization, send the image to a vision model with prompt "Describe this image in one sentence"
2. Replace the `ImageContent` part with a `TextContent` part containing `[Image description: {description}]`
3. This preserves image context in text form without the token cost of vision content

- [ ] **Step 2: Test, commit**

```bash
git commit -m "feat: evict images from context with vision model descriptions"
```

---

## Chunk 6: Slice 6 — Full Dynamic UI

### Task 22: Implement Wizard Flow

**Files:**
- Create: `src/Core/Contracts/UI/WizardState.cs` (WizardStep already exists)
- Modify: `src/Agents/UI/UISession.cs`
- Test: `test/Core.Tests/UI/WizardTests.cs`

- [ ] **Step 1: Write tests for multi-step wizard**

Test StartWizard, AdvanceWizard (with button selection), AdvanceWizard (with free text), wizard completion.

- [ ] **Step 2: Add wizard methods to IUISession**

```csharp
Task<WizardState> StartWizard(string wizardId, WizardStep[] steps, string projectSlug, CancellationToken ct);
Task<WizardState> AdvanceWizard(string wizardId, string selection, CancellationToken ct);
```

- [ ] **Step 3: Implement wizard state machine in UISession**

Handle `"wz:"` prefix in callback routing. Track current step, collected values. Handle FreeText steps by setting pending free text state.

- [ ] **Step 4: Wire wizard.started event in StreamSubscriber**

When `"wizard.started"` event received, render first step with inline keyboard.

- [ ] **Step 5: Run tests, commit**

```bash
git commit -m "feat: implement multi-step wizard flow in UISession"
```

---

### Task 23: Implement Paginator and Menu

**Files:**
- Create: `src/Core/Contracts/UI/PaginatorState.cs`
- Create: `src/Core/Contracts/UI/MenuState.cs`
- Modify: `src/Agents/UI/UISession.cs`
- Test: `test/Core.Tests/UI/PaginatorTests.cs`
- Test: `test/Core.Tests/UI/MenuTests.cs`

- [ ] **Step 1: Write tests for paginator (next/prev, page bounds)**

- [ ] **Step 2: Write tests for menu navigation (children, breadcrumb, back)**

- [ ] **Step 3: Implement paginator**

Handle `"pg:"` prefix. Track current page, compute visible items for page.

- [ ] **Step 4: Implement hierarchical menu**

Handle `"mn:"` prefix. Navigate tree structure, maintain breadcrumb path.

- [ ] **Step 5: Run tests, commit**

```bash
git commit -m "feat: implement paginator and hierarchical menu widgets"
```

---

### Task 24: Implement Form Widget + Widget Cleanup

**Files:**
- Create: `src/Core/Contracts/UI/FormState.cs`
- Modify: `src/Agents/UI/UISession.cs`
- Test: `test/Core.Tests/UI/FormTests.cs`

- [ ] **Step 0: Write tests for form state machine**

Test SingleChoice field advancement, MultiChoice toggle (add/remove from selected set), FreeText field routing via `HasPendingFreeTextInput`, and form completion.

- [ ] **Step 1: Implement form state machine**

Handle `"fm:"` prefix. Iterate through FormField list, collect values per field. Support SingleChoice, MultiChoice (via button toggles with a `HashSet<string>` tracked in state), and FreeText fields. For FreeText fields, set `HasPendingFreeTextInput` to true so `TelegramBotService` routes the next text message to `UISession` instead of the Project grain (same pattern as wizard FreeText from Task 22). `WizardState.Collected` and `FormState.Values` are `IReadOnlyDictionary` — advancing creates a new record instance with updated values (immutable state pattern for Orleans journaling).

- [ ] **Step 2: Implement widget cleanup via Orleans reminder**

Register an Orleans reminder named `"widget-cleanup"` with period `TimeSpan.FromMinutes(5)` in UISession. In `ReceiveReminder`, iterate `activeWidgets` dictionary and remove entries older than their timeout:
- Paginators: 30 min timeout
- Menus: 10 min timeout
- Wizards/Forms with no activity: 60 min timeout
Publish a `WidgetExpiredEvent` for each removed widget so `StreamSubscriber` can call `editMessageReplyMarkup` to remove dead buttons from the Telegram message.

- [ ] **Step 3: Run full test suite**

Run: `dotnet test IAW.slnx -v m`
Expected: ALL tests pass

- [ ] **Step 4: Run aspire and manual end-to-end test**

Run: `aspire run`
Verify: All 6 slices work together — multimodal chat, approvals with buttons, file upload + RAG, dashboard, context providers, wizards/paginators/menus.

- [ ] **Step 5: Final commit**

```bash
git commit -m "feat: implement form widget and widget cleanup with Orleans reminders"
```

---

## Final Verification

- [ ] **Full build:** `dotnet build IAW.slnx`
- [ ] **Full test suite:** `dotnet test IAW.slnx -v m`
- [ ] **Aspire run:** `aspire run` — verify all containers healthy, bot responsive
- [ ] **Manual Telegram test:** Send text, voice, photo, PDF in different forum topics. Verify topic isolation, dashboard, approvals, RAG search.
