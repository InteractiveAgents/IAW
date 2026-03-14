# Telegram Bot Refactor — Full Design Specification

**Date:** 2026-03-14
**Status:** Draft
**Scope:** IAW Telegram client, Core models, Orleans grain architecture, RAG pipeline, dynamic UI

---

## Overview

Refactor the Telegram bot from a basic text relay into a production-ready, multi-tenant personal assistant platform. Introduces project-based isolation via forum topics, multimodal message support, document RAG, dynamic inline UI, and async event-driven updates.

## Design Principles

- **Projects are first-class** — each forum topic is an independent project with its own history, tasks, schedules, and knowledge base
- **Agents don't know about Telegram** — they emit abstract intents, the Telegram client translates to platform-specific UI
- **Multimodal from the ground up** — ChatMessage supports text, images, and files natively
- **Local-first with cloud option** — local embedding models and Azurite by default, cloud services as opt-in

---

## 1. Core Model Changes

### 1.1 ChatMessage — Multimodal

Current `ChatMessage` is text-only. New model uses discriminated content parts.

```csharp
[GenerateSerializer]
public sealed record ChatMessage
{
    [Id(0)] public string Role { get; init; }
    [Id(1)] public string Content { get; init; }             // kept for serialization compat
    [Id(2)] public DateTimeOffset TimestampUtc { get; init; }
    [Id(3)] public IReadOnlyList<ContentPart> Parts { get; init; } = [];  // NEW at [Id(3)]

    // convenience: Parts first, falls back to Content for old messages
    public string Text => Parts.Count > 0
        ? string.Join("", Parts.OfType<TextContent>().Select(p => p.Text))
        : Content ?? string.Empty;
}

[GenerateSerializer]
[JsonDerivedType(typeof(TextContent))]   // for JSON serialization (MCP, HTTP APIs)
[JsonDerivedType(typeof(ImageContent))]  // Orleans polymorphism handled by [GenerateSerializer]
[JsonDerivedType(typeof(FileContent))]   // on each concrete type — no extra config needed
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

### 1.2 Serialization Migration Strategy

`ChatMessage.Content` (`[Id(1)]`, string) is **preserved** for backward compatibility with existing durable grain state. The new `Parts` field uses `[Id(3)]` so Orleans deserialization of old journals succeeds — old messages load with `Content` populated and `Parts` empty, new messages populate both. The `.Text` property checks `Parts` first, falls back to `Content`.

All new code writes to `Parts`. `Content` is set to `Text` for backward-compatible consumers. `Content` is deprecated but not removed until a clean-slate migration.

`DurableChatHistoryProvider` handles both paths: if `Parts` is non-empty, converts `ContentPart` list to M.E.AI `ChatMessage` with `TextContent` + `ImageContent` (for vision models). If `Parts` is empty, falls back to `Content` string. `FileContent` converted to text annotation `[File: report.pdf (ingested)]`.

### 1.3 EmbeddingModel Base Class

Mirrors `LLMModel` pattern:

```csharp
public abstract class EmbeddingModel
{
    // mirrors LLMModel pattern exactly: constructor auto-registration, list-based registry
    static readonly Lock _lock = new();
    static readonly List<EmbeddingModel> _registry = [];

    public abstract string Id { get; }
    public abstract string Provider { get; }
    public abstract int Dimensions { get; }
    public abstract string DisplayName { get; }

    // exact same ServiceKey formula as LLMModel
    public string ServiceKey
    {
        get
        {
            var normalizedId = Id.ToLowerInvariant().Replace(".", "").Replace(":", "-");
            return $"{Provider.ToLowerInvariant()}-{normalizedId}";
        }
    }

    protected EmbeddingModel()
    {
        lock (_lock) _registry.Add(this);
    }

    public static IReadOnlyList<EmbeddingModel> All { get { lock (_lock) return [.. _registry]; } }
}

public sealed class MxbaiEmbedLarge : EmbeddingModel
{
    public override string Id => "mxbai-embed-large";
    public override string Provider => "ollama";
    public override int Dimensions => 1024;
    public override string DisplayName => "MxBai Embed Large";
}

public sealed class TextEmbedding3Small : EmbeddingModel
{
    public override string Id => "text-embedding-3-small";
    public override string Provider => "openai";
    public override int Dimensions => 1536;
    public override string DisplayName => "Text Embedding 3 Small";
}
```

Injected via `[Embedding<MxbaiEmbedLarge>]` attribute resolving to keyed `IEmbeddingGenerator<string, Embedding<float>>`.

Registration in AppHost via `WithEmbedding<T>()` mirroring `WithLLM<T>()`.

---

## 2. Grain Architecture

### 2.1 Three New Grain Types

**IProject** — per-user, per-project orchestrator:

```
Key: "{telegramId}/{projectSlug}"
Extends: Agent
GrainType: "project-v1"    // distinct from Agent's "agent-v3" to avoid key collisions

Durable State (in addition to Agent base):
  IDurableList<ProjectTask>                    tasks
  IDurableDictionary<string, ScheduledJob>     schedules
  IDurableDictionary<string, FileReference>    files
  IDurableDictionary<string, string>           projectMeta

Interface:
  // inherited from IAgent
  GetResponseStream(prompt, ct)
  GetHistory() / ClearHistory()

  // project-specific
  GetDashboard(ct) -> ProjectDashboard
  AddTask(description, priority, ct) -> ProjectTask
  UpdateTask(taskId, status, ct)
  GetTasks(filter?, ct) -> IReadOnlyList<ProjectTask>
  ScheduleJob(name, interval, description, ct) -> ScheduledJob
  CancelJob(jobId, ct)
  RegisterFile(fileRef, ct)
  RequestApproval(question, options, ct)
  GetProjectContext(ct) -> ProjectContext
```

**IUserProfile** — per-user, cross-project:

```
Key: "{telegramId}"
Extends: DurableGrain (not Agent — no LLM, but needs durable state)
GrainType: "user-profile-v1"

Durable State:
  IDurableDictionary<string, string>     preferences
  IDurableDictionary<string, string>     projects      // slug -> topicId
  IDurableList<MemoryEntry>              memories

Interface:
  GetPreferences(ct) -> Dictionary<string, string>
  SetPreference(key, value, ct)
  GetProjects(ct) -> IReadOnlyList<ProjectInfo>
  RegisterProject(slug, topicId, ct)
  RemoveProject(slug, ct)
  ResolveProject(topicId, ct) -> string?
  RememberFact(fact, ct)
  RecallFacts(query, ct) -> IReadOnlyList<string>
```

**IUISession** — per-user UI state:

```
Key: "{telegramId}"
Extends: DurableGrain (not Agent — no LLM, but needs durable state)
GrainType: "ui-session-v1"

Durable State:
  IDurableDictionary<string, WidgetState>    activeWidgets
  IDurableDictionary<string, PendingApproval> pendingApprovals
  IDurableDictionary<string, WizardState>    activeWizards

Interface:
  RenderButtons(messageContext, buttons, ct) -> InlineMarkup
  HandleCallback(callbackId, data, ct) -> CallbackResult
  StartWizard(wizardId, steps, ct) -> WizardState
  AdvanceWizard(wizardId, selection, ct) -> WizardState
  RegisterApproval(approvalId, question, options, projectSlug, ct)
  ResolveApproval(approvalId, decision, ct) -> ApprovalResult
  GetPaginator(listId, items, pageSize, ct) -> Page
  NavigatePage(listId, direction, ct) -> Page
  HasPendingFreeTextInput(topicId, ct) -> bool
```

### 2.2 IAgent Interface Extension

The existing `IAgent.GetResponseStream(string prompt, CancellationToken ct)` only accepts text. A new overload is needed for multimodal input:

```csharp
IAsyncEnumerable<string> GetResponseStream(ChatMessage message, CancellationToken ct);
```

The string-based overload is preserved for backward compatibility and internally wraps the string into a `ChatMessage` with a single `TextContent` part. The `Project` grain's `TelegramBotService` caller uses the `ChatMessage` overload. The `Agent` base class `EnrichWithContext` method is updated to work with `ChatMessage` instead of a plain string — context providers append to the Parts list rather than string concatenation.

### 2.3 Retired Types

`PersonalAssistant` and `IPersonalAssistant` are retired. Their responsibilities split:

- **User-level state** (preferences, memories) -> `UserProfile` grain
- **Project orchestration** (tasks, delegation, tools) -> `Project` grain
- **Message receivers** (`IReceiver<TaskCompletedMessage>`, `IReceiver<TaskFailedMessage>`, etc.) -> `Project` grain implements these same interfaces, scoped per-project
- **MCP `assistant_chat` tool** -> updated to route through `UserProfile.ResolveProject()` then to the appropriate `Project` grain. A new `project_chat` tool is added alongside or replaces `assistant_chat`.
- **DevUI** -> updated to discover `IProject` grains via the agent registry. DevUI can list all active projects and connect to any of them.
- **RememberFact / RecallMemories** -> moved to `UserProfile` grain (cross-project) and also available as tools on `Project` grain (project-scoped via `RAGContextProvider`)

### 2.4 Message Flow

```
Telegram Update (text/photo/file in forum topic)
  |
  +-> TelegramBotService
  |     +-- identifies telegramId + topicId
  |     +-- calls UserProfile.ResolveProject(topicId) -> projectSlug
  |     +-- if file/photo: uploads to Azure Blob, gets blobUri
  |     +-- builds multimodal ChatMessage
  |     +-- calls Project.GetResponseStream(message)
  |
  +-> Project grain
  |     +-- enriches with context providers (see Section 5)
  |     +-- chat reducer trims history
  |     +-- sends to LLM with tools
  |     +-- streams response back
  |
  +-> TelegramBotService (response)
  |     +-- progressive edits for streaming text
  |     +-- if approval requested: UISession.RegisterApproval() -> render inline buttons
  |     +-- if tasks changed: update pinned dashboard message
  |     +-- if file generated: upload to Telegram
  |
  +-> Orleans Streams (async)
        +-- task.completed -> update dashboard + notify user
        +-- approval.resolved -> route decision back to Project
        +-- schedule.triggered -> Project processes job -> notify user
```

### 2.5 Message Routing Priority

```
Incoming text message in a project topic:
  |
  1. UISession.HasPendingFreeTextInput(telegramId, topicId)?
  |   YES -> route to UISession.AdvanceWizard() / ResolveApproval()
  |   NO  -> continue
  |
  2. Is this a reply to an approval message?
  |   YES -> route as clarification to Project grain with approval context
  |   NO  -> continue
  |
  3. Normal message -> Project.GetResponseStream()
```

### 2.6 Callback Routing

```
User taps inline button -> Telegram CallbackQuery
  |
  +-> TelegramBotService
  |     +-- parses callback data: "{type}:{id}:{action}"
  |     +-- calls UISession.HandleCallback(callbackId, data)
  |
  +-> UISession grain
  |     +-- routes by type:
  |           "ap" -> ResolveApproval() -> publishes to Project via stream
  |           "wz" -> AdvanceWizard() -> returns next step markup
  |           "pg" -> NavigatePage() -> returns new page
  |           "mn" -> navigates menu tree -> returns new keyboard
  |           "fm" -> advances form -> returns next field
  |           "tk" -> task action -> routes to Project
  |     +-- returns CallbackResult (new text, new markup, toast)
  |
  +-> TelegramBotService
        +-- answerCallbackQuery
        +-- editMessageText (if text changed)
        +-- editMessageReplyMarkup (if buttons changed)
```

---

## 3. File Storage + RAG Pipeline

### 3.1 Infrastructure

```csharp
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(e => e.WithDataVolume("iaw-blobs"));
var blobs = storage.AddBlobs("file-storage");

var qdrant = builder.AddQdrant("qdrant")
    .WithDataVolume("iaw-qdrant");

var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>()
    .WithLLM<Claude45Haiku>()
    .WithEmbedding<MxbaiEmbedLarge>()
    .WithEmbedding<TextEmbedding3Small>()
    .WithState<FileSystemStateProvider>(stateRootDirectory: "D:\\IAW")
    .WithFileStorage(blobs)
    .WithVectorStore(qdrant);
```

### 3.2 File Upload Flow

1. `TelegramBotService` downloads file from Telegram via `getFile(fileId)`
2. Uploads to Azure Blob: `file-storage/{telegramId}/{projectSlug}/{guid}-{filename}`
3. Builds `ChatMessage` with `FileContent` part (blobUri, fileName, mimeType, sizeBytes, ingested: false)
4. `Project` grain receives message, stores in history, registers file
5. If PDF: triggers async ingestion

### 3.3 PDF Ingestion Pipeline

1. Download blob
2. PdfPig extracts text per page (layout-aware with `DocstrumBoundingBoxes`)
3. SemanticKernel `TextChunker` splits into ~200 token chunks
4. Each chunk embedded via `IEmbeddingGenerator<MxbaiEmbedLarge>`
5. Stored in Qdrant collection: `project-{telegramId}-{projectSlug}`
6. `FileContent.Ingested` updated to true
7. Dashboard updated, user notified

### 3.4 Image Handling

Photos downloaded and uploaded to blob storage. `ImageContent` in `ChatMessage` is converted to M.E.AI `ImageContent` by `DurableChatHistoryProvider`, passed to vision-capable models (Sonnet 4.6). No Qdrant ingestion for images.

### 3.5 Qdrant Collection Strategy

One collection per project:

```
Collection: "project-{telegramId}-{projectSlug}"
  vector size: depends on embedding model (1024 for mxbai, 1536 for text-embedding-3-small)
  metadata: documentId, pageNumber, fileName, ingestedAt
  distance: cosine
```

Per-project isolation: search returns only that project's documents, deleting a project drops the collection. Collection created lazily on first document upload. Embedding model tracked in `projectMeta["embeddingModel"]` — switching models requires re-ingestion.

**Scaling note:** At large scale (hundreds of users, thousands of projects), per-project collections may hit Qdrant performance limits. If this becomes an issue, migrate to a single shared collection with a `projectKey` payload filter. Per-project collections are the starting design for isolation simplicity. Default embedding model for new projects is `MxbaiEmbedLarge` (1024-dim). If the configured model is unavailable at ingestion time, ingestion fails with an error notification rather than silently using a different model.

**Ingestion status tracking:** File ingestion status is tracked in the `files` dictionary (`FileReference.Ingested`) rather than mutating the immutable `FileContent` inside `ChatMessage.Parts`. The `FileContent.Ingested` field in the ChatMessage is set at message creation time and not updated — it reflects the state at time of message, while `FileReference` in the `files` dictionary reflects current ingestion state.

---

## 4. Dashboard + Task Board + Schedules

### 4.1 Project Task Model

```csharp
[GenerateSerializer]
public sealed record ProjectTask
{
    [Id(0)] public string Id { get; init; }
    [Id(1)] public string Description { get; init; }
    [Id(2)] public TaskPriority Priority { get; init; }
    [Id(3)] public ProjectTaskStatus Status { get; init; }
    [Id(4)] public string? AssignedAgent { get; init; }
    [Id(5)] public DateTimeOffset CreatedAt { get; init; }
    [Id(6)] public DateTimeOffset? CompletedAt { get; init; }
    [Id(7)] public string? Result { get; init; }
}

[GenerateSerializer]
public enum TaskPriority { Low, Medium, High, Critical }

[GenerateSerializer]
public enum ProjectTaskStatus { Pending, InProgress, Done, Cancelled }
```

### 4.2 Scheduled Job Model

```csharp
[GenerateSerializer]
public sealed record ScheduledJob
{
    [Id(0)] public string Id { get; init; }
    [Id(1)] public string Name { get; init; }
    [Id(2)] public string Description { get; init; }
    [Id(3)] public TimeSpan Interval { get; init; }       // Orleans reminders use TimeSpan
    [Id(4)] public DateTimeOffset NextRunAt { get; init; } // computed from Interval
    [Id(5)] public DateTimeOffset? LastRunAt { get; init; }
    [Id(6)] public string? LastResult { get; init; }
    [Id(7)] public bool Active { get; init; }
}
```

Scheduled jobs map to Orleans reminders via the existing `IRemindable` infrastructure from the `Agent` base class. When a reminder fires, `Project` feeds the job's `Description` as a prompt to itself, executes tools, and publishes results.

### 4.3 Dashboard — Pinned Message

Each project topic has one pinned message as a live dashboard. `Project` grain tracks `pinnedMessageId` in `projectMeta`.

Dashboard content:

```
project-name

> Active (2)
  task1 description — agent working...
  task2 description — 3/5 passing

> Done (4)
  task3 description
  task4 description
  ...N more

> Scheduled
  job1 — next: 09:00
  job2 — next: 14:00

> Files (2)
  report.pdf (indexed)
  architecture.jpg

Updated: 2 min ago
```

### 4.4 Dashboard Update Flow

1. State change in `Project` grain (task added/completed, job ran, file uploaded)
2. `Project` calls `BuildDashboard()` to generate MarkdownV2 string
3. Publishes `DashboardChangedEvent` to Orleans stream
4. `StreamSubscriber` in Telegram client receives event
5. Calls `editMessageText(chatId, pinnedMessageId, newText, MarkdownV2)`
6. 2-second debounce **per-project** in `StreamSubscriber` using `ConcurrentDictionary<string, Timer>` keyed by project slug. On Telegram client restart, `pinnedMessageId` is recovered from `Project.GetDashboard()` and subscriptions are re-established

### 4.5 Dashboard Creation

New project (first message to new topic or `/newproject`):

1. `UserProfile.RegisterProject(slug, topicId)`
2. `Project` grain activates with default state
3. Telegram client sends initial dashboard message
4. Pins it via `pinChatMessage(chatId, messageId)`
5. Stores `pinnedMessageId` in project state

### 4.6 Task Lifecycle

**LLM-driven:** User asks to create a task -> LLM calls `AddTask` tool -> durable state updated -> dashboard updated.

**Button-driven:** User taps done/cancel button on task -> CallbackQuery routed via UISession to `Project.UpdateTask()` -> dashboard updated.

**Agent-driven (async):** Project assigns task to downstream agent -> agent publishes `StepProgressEvent` / `TaskCompletedEvent` -> Project receives via stream -> updates task -> dashboard updated -> user notified.

### 4.7 Notification Strategy

| Event | Notification |
|-------|-------------|
| Task completed | New message in topic + dashboard update |
| Task step progress | Dashboard update only |
| Approval needed | New message with inline buttons |
| Scheduled job ran | New message only if result is notable |
| File ingested | Edit original "indexing..." message to show completion |
| Error/failure | New message with details |

---

## 5. Context Management + Chat Reducers

### 5.1 Context Provider Chain

Each `Project` grain assembles LLM context from providers, ordered by priority:

1. **UserContextProvider** — from `UserProfile` grain: preferences, timezone, language, general facts
2. **ProjectContextProvider** — project description, goals, constraints, file inventory
3. **TaskContextProvider** — active tasks, recent completions, pending approvals
4. **RAGContextProvider** — Qdrant search results for current query (top 5 chunks)
5. **MemoryContextProvider** — cross-project user memories (existing)

### 5.2 Chat Reduction Strategy

Full history always preserved in durable state. Reduction only affects what enters the LLM context window.

**Three tiers:**

- **Tier 1: Last message** — always full, never reduced. All content parts intact.
- **Tier 2: Recent window (last 20 messages)** — verbatim including multimodal references and tool calls.
- **Tier 3: Summarized history (messages 21+)** — LLM-generated summary block preserving key decisions, task assignments, approval outcomes, file references.

**Non-reducible messages** survive summarization as verbatim quotes:
- Approval decisions
- Task creation/completion
- File uploads (FileContent references)
- Explicit "remember this" instructions

### 5.3 Summarization Trigger

When history exceeds 40 messages:

1. Take messages 21-40
2. Extract non-reducible messages
3. Send remainder to LLM for summarization
4. Store summary as synthetic `ChatMessage` (role: "system")
5. Replace messages 21-40 with summary + non-reducible messages
6. Full unreduced history stays in `IDurableList` permanently

### 5.4 Context Budget

Approximate per-call allocation:

```
System prompt + instructions:     ~500 tokens
User context (prefs, facts):      ~200 tokens
Project context (meta, tasks):    ~300 tokens
RAG context (top 5 chunks):       ~1000 tokens
Summarized history:               ~500 tokens
Recent 20 messages:               ~3000 tokens
Current message:                  ~500 tokens
Tool definitions:                 ~800 tokens
Total:                            ~6800 tokens
```

### 5.5 Images in Context

- Recent window (last 20): passed as vision content to vision-capable models
- Summarized tier: replaced with text descriptions generated by vision model before eviction
- Token cost: ~1000-2000 per image, managed by keeping vision content only in recent window

---

## 6. Telegram UI Framework

### 6.1 Widget State Types

**`[Id]` numbering convention:** Base `WidgetState` uses `[Id(0)]` through `[Id(9)]` (reserved). Derived types start at `[Id(10)]` to avoid collisions if base class fields are added later.

```csharp
[GenerateSerializer]
public abstract record WidgetState
{
    [Id(0)] public string Id { get; init; }
    [Id(1)] public string ProjectSlug { get; init; }
    [Id(2)] public int MessageId { get; init; }
}

[GenerateSerializer]
public sealed record ButtonGridState : WidgetState
{
    [Id(10)] public IReadOnlyList<ButtonRow> Rows { get; init; }
    [Id(11)] public string? SelectedValue { get; init; }
}

[GenerateSerializer]
public sealed record PaginatorState : WidgetState
{
    [Id(10)] public IReadOnlyList<string> Items { get; init; }
    [Id(11)] public int PageSize { get; init; }
    [Id(12)] public int CurrentPage { get; init; }
}

[GenerateSerializer]
public sealed record WizardState : WidgetState
{
    [Id(10)] public IReadOnlyList<WizardStep> Steps { get; init; }
    [Id(11)] public int CurrentStep { get; init; }
    [Id(12)] public IReadOnlyDictionary<string, string> Collected { get; init; }
}

[GenerateSerializer]
public sealed record MenuState : WidgetState
{
    [Id(10)] public MenuNode Root { get; init; }
    [Id(11)] public IReadOnlyList<string> BreadCrumb { get; init; }
}

[GenerateSerializer]
public sealed record FormState : WidgetState
{
    [Id(10)] public IReadOnlyList<FormField> Fields { get; init; }
    [Id(11)] public int CurrentField { get; init; }
    [Id(12)] public IReadOnlyDictionary<string, string> Values { get; init; }
}
```

### 6.2 Supporting Types

```csharp
[GenerateSerializer]
public sealed record ButtonRow([Id(0)] IReadOnlyList<Button> Buttons);

[GenerateSerializer]
public sealed record Button(
    [Id(0)] string Text,
    [Id(1)] string CallbackData,
    [Id(2)] string? Url);

[GenerateSerializer]
public sealed record WizardStep(
    [Id(0)] string Id,
    [Id(1)] string Prompt,
    [Id(2)] IReadOnlyList<Button> Options);

[GenerateSerializer]
public sealed record MenuNode(
    [Id(0)] string Label,
    [Id(1)] string? Action,
    [Id(2)] IReadOnlyList<MenuNode>? Children);

[GenerateSerializer]
public sealed record FormField(
    [Id(0)] string Name,
    [Id(1)] string Prompt,
    [Id(2)] FormFieldType Type,
    [Id(3)] IReadOnlyList<Button>? Options);

[GenerateSerializer]
public enum FormFieldType { SingleChoice, MultiChoice, FreeText }
```

### 6.3 Callback Data Format

64-byte limit. Short prefixes for routing:

```
Format: "{type}:{id}:{action}"

ap:abc123:yes        approval approved
ap:abc123:no         approval declined
wz:setup:next        wizard advance
wz:setup:back        wizard back
wz:setup:opt:high    wizard option selected
pg:tasks:next        paginator next page
pg:tasks:prev        paginator prev page
mn:settings:notif    menu navigate
mn:settings:back     menu go up
fm:newtask:opt:high  form option selected
tk:abc123:done       task mark done
tk:abc123:cancel     task cancel
```

### 6.4 Agent-to-UI Translation

Agents emit abstract intents via tools and events. They do not know about Telegram.

Project grain tools:
- `RequestApproval(question, options)` -> publishes `approval.requested` event
- `PresentOptions(question, options)` -> publishes `options.presented` event
- `StartWizard(name, steps)` -> publishes `wizard.started` event

`StreamSubscriber` in Telegram client receives events, creates widgets via `UISession`, renders `InlineKeyboardMarkup`, sends Telegram messages.

### 6.5 FreeText in Wizards

When a wizard step is `FormFieldType.FreeText`:
1. Bot sends prompt as regular message
2. `UISession` tracks pending free text state
3. Next text message intercepted by `TelegramBotService`
4. Routed to `UISession.AdvanceWizard()` instead of `Project`
5. Wizard advances

### 6.6 Telegram Message Length Limits

Telegram enforces a 4096-character limit for text messages. Both dashboard content and streaming responses can exceed this.

**Dashboard:** If dashboard content exceeds 4096 chars, truncate the "Done" section (show only last 3 completed tasks with "...N more") and the "Files" section (show count only). If still over limit, split into two messages (dashboard summary pinned, full task list as a second message with paginator).

**Streaming responses:** If the accumulated streamed response exceeds 4000 chars during progressive edits, stop editing the current message and send a new continuation message. Link them with "...(continued)" / "(continued from above)...".

**Agent responses:** Final responses from the LLM are split at sentence boundaries if they exceed 4096 chars, sent as multiple messages.

### 6.7 Widget Cleanup

- Approvals: buttons replaced with result text on resolution
- Wizards/Forms: message edited to summary on completion/cancellation
- Paginators: expire after 30 minutes, buttons removed
- Menus: expire after 10 minutes, buttons removed
- `UISession` runs periodic cleanup via Orleans reminder

---

## 7. Build Order — Vertical Slices

### Slice 1: Multimodal Chat + Project Grain + Topic Routing

- Extend `ChatMessage` to multimodal with `ContentPart` types (preserving `[Id(1)]` compat)
- Add `GetResponseStream(ChatMessage)` overload to `IAgent`
- Build `Project : Agent` grain with `[GrainType("project-v1")]`, basic chat (history, LLM, tools)
- Build `UserProfile : DurableGrain` with project registry and state injection
- Build hosting extensions: `WithFileStorage()`, `WithVectorStore()`, `WithState<T>()` on `IAWExtensions`
- Refactor `TelegramBotService` to route by forum topic via `UserProfile.ResolveProject()`
- Update `DurableChatHistoryProvider` for multimodal (dual-path: Parts vs Content fallback)
- Result: per-project chat works via forum topics

### Slice 2: UISession + Inline Keyboards + Approvals

- Build `UISession : DurableGrain` with `[GrainType("ui-session-v1")]` and widget state management
- Implement approval flow (RequestApproval tool -> inline buttons -> callback routing)
- Implement basic `ButtonGrid` widget
- Add callback routing in `TelegramBotService`
- Implement message routing priority (pending free text -> reply to approval -> normal)
- Result: bot can ask approval questions with inline buttons

### Slice 3: File Storage + Qdrant RAG + Embedding Infrastructure

- Add Azure Blob (Azurite + data volume) and Qdrant (+ data volume) to AppHost
- Build `EmbeddingModel` base class (mirrors `LLMModel`: constructor auto-registration, list-based registry)
- Build `[Embedding<T>]` attribute + `EmbeddingAttributeMapper<T>` for DI
- Build `WithEmbedding<T>()` hosting extension
- Implement file upload flow (Telegram -> Blob -> ChatMessage with FileContent)
- Implement PDF ingestion pipeline (PdfPig + TextChunker + Qdrant)
- Build `RAGContextProvider` for project-scoped document search
- Implement image handling with vision models
- Handle Telegram 4096-char limit for streaming responses
- Result: users can upload docs, bot answers questions from them

### Slice 4: Dashboard + Task Board + Schedules

- Add `ProjectTask` and `ScheduledJob` to `Project` grain state
- Implement task management tools (AddTask, UpdateTask, GetTasks)
- Implement scheduled jobs via Orleans reminders
- Build dashboard rendering and pinned message management
- Wire `DashboardChangedEvent` through Orleans streams to `StreamSubscriber`
- 2-second debounce on dashboard edits
- Result: live task dashboard in each project topic

### Slice 5: Context Providers + Chat Reducers

- Build full context provider chain (User, Project, Task, RAG, Memory)
- Build `UserContextProvider` (queries `UserProfile` grain)
- Build `ProjectContextProvider` (project meta, goals, file inventory)
- Build `TaskContextProvider` (active tasks, recent completions, pending approvals)
- Implement 3-tier chat reduction with non-reducible message pinning
- Implement summarization trigger at 40 messages
- Image eviction: vision model describes images before they leave recent window
- Result: production-ready context management

### Slice 6: Full Dynamic UI

- Implement `WizardState` + multi-step wizard flow
- Implement `PaginatorState` + paginated list navigation
- Implement `MenuState` + hierarchical menu navigation
- Implement `FormState` + structured input collection
- Add FreeText field support with message interception
- Widget cleanup via Orleans reminder
- Result: full interactive Telegram UI toolkit

---

## 8. Implementation Details

### 8.1 Project Grain Durable State Composition

The `Agent` base class takes `[AgentState] AgentDurableState` via constructor injection. `Project` extends `Agent` but needs additional durable collections (tasks, schedules, files, projectMeta). Two approaches:

**Chosen approach:** Create a `ProjectDurableState` that extends `AgentDurableState` with the additional collections, and a corresponding `[ProjectState]` attribute + `ProjectStateMapper`. The `Project` constructor takes `[ProjectState] ProjectDurableState` which the mapper resolves, creating both the base agent collections and the project-specific ones from the same journaling factory.

```csharp
public sealed class ProjectDurableState : AgentDurableState
{
    public required IDurableList<ProjectTask> Tasks { get; init; }
    public required IDurableDictionary<string, ScheduledJob> Schedules { get; init; }
    public required IDurableDictionary<string, FileReference> Files { get; init; }
    public required IDurableDictionary<string, string> ProjectMeta { get; init; }
}
```

### 8.2 Non-Agent DurableGrain Pattern (UserProfile, UISession)

`UserProfile` and `UISession` extend `DurableGrain` but are not agents (no LLM, no tools). They need their own state injection pattern:

```csharp
// attribute + mapper for each grain type, or a generic [DurableState] attribute
public class UserProfile(
    [UserProfileState] UserProfileDurableState state)
    : DurableGrain, IUserProfile
{
    // state.Preferences, state.Projects, state.Memories available
}
```

Each non-Agent durable grain type gets its own state class + attribute + mapper. This establishes the pattern for any future non-Agent grains that need journaled state.

### 8.3 EnrichWithContext Migration

The current `EnrichWithContext(string prompt)` method and `IAgentContextProvider.GetContextAsync()` returning `IReadOnlyList<string>` are **preserved unchanged**. The string-based `GetResponseStream(string)` overload continues to use them as-is.

The new `GetResponseStream(ChatMessage)` overload:
1. Extracts the text from the ChatMessage via `.Text`
2. Passes the text to existing context providers (they still return strings)
3. Wraps context strings as `TextContent` parts prepended to the message
4. Passes multimodal parts (images, file references) through to the M.E.AI `ChatMessage` conversion
5. `IAgentContextProvider` interface does NOT change — context is always text-based

This avoids breaking existing providers while supporting multimodal messages.

### 8.4 Dynamic Project Discovery

Dynamically-created `Project` grains are not statically discoverable by `InterfaceCatalog` (which scans types at startup). For MCP `agent_list_all` and DevUI:
- `UserProfile.GetProjects()` returns the list of active projects for a user
- A new MCP tool `project_list(telegramId)` queries this
- DevUI adds a project browser that queries `UserProfile` grains
- The existing `AgentRegistryGrain` is not used for dynamic Project grains — it remains for static infrastructure agents (Roslyn, DotNet, GitHub, etc.)

### 8.5 Stream Re-subscription on Telegram Client Restart

When the Telegram client (Orleans client, not a silo) restarts:
1. On startup, enumerate all `UserProfile` grains with active projects (stored in a lightweight registry or by querying known user IDs from Telegram webhook state)
2. For each active project, re-subscribe to its Orleans streams (dashboard changes, notifications, approvals)
3. Recover `pinnedMessageId` for each project via `Project.GetDashboard()`
4. The Telegram client should persist its known user/project set to survive restarts (can be stored in the Azurite blob or a local file)

---

## 9. Key Dependencies

| Package | Purpose |
|---------|---------|
| Telegram.BotAPI | Telegram Bot API client (existing) |
| PdfPig | PDF text extraction |
| Microsoft.SemanticKernel.Core | TextChunker for document chunking |
| Aspire.Hosting.Qdrant | Qdrant vector store hosting |
| Qdrant.Client | Qdrant .NET client |
| Azure.Storage.Blobs | Azure Blob Storage client |
| Aspire.Hosting.Azure.Storage | Azurite emulator hosting |
| Microsoft.Extensions.AI | IEmbeddingGenerator abstraction |
| Microsoft.Extensions.AI.OpenAI | OpenAI embedding provider |

---

## 10. Retired Components

- `PersonalAssistantAgent` — replaced by `Project` grain
- `IPersonalAssistant` — replaced by `IProject`
- Single-grain routing — replaced by per-user/per-project topology
- Text-only `ChatMessage.Content` — replaced by `ChatMessage.Parts`
