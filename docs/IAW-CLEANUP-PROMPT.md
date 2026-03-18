# IAW Codebase: Production Hardening & Cleanup — Master Prompt

> **Purpose**: This prompt is a comprehensive work order for Claude Code to transform IAW from a well-architected prototype into a production-grade AI assistant. Execute each phase in order. Run `dotnet build IAW.slnx` and `dotnet test IAW.slnx` after every major change to verify nothing is broken. Commit after each completed phase with a descriptive message.

---

## PHASE 0: ORIENTATION (Read-Only — Do Not Change Code Yet)

Before making any changes, read and internalize these files to understand the full architecture:

```
CLAUDE.md
src/Core/Agents/Agent.cs          — base agent class (core conversation loop)
src/Core/Agents/Agent.*.cs        — all 7 partial files
src/Core/Memory.cs                — memory base class
src/Core/Contracts/IAgent.cs      — primary grain interface
src/Core/Orchestration/*.cs       — ScriptGenerator, ScriptExecutor, CheckpointStore, OrchestrationPlan
src/Agents/Orchestration/*.cs     — PersonalAssistant, TaskSupervisor, Planning, CodeOrchestrator, Deployer
src/Agents/Memory/*.cs            — all 5 memory agents + interfaces
src/Agents/Review/*.cs            — Reviewer, SelfImprovement
src/Agents/Infrastructure/*.cs    — FileSystem, Shell, Git, Build, Aspire
src/Core/Context/*.cs             — all context providers
src/Core/Communication/*.cs       — IReceiver, IStreamConsumer, IStreamProducer
test/Core.Tests/ArchitectureGuardTests.cs
test/Core.Tests/ArchitectureGuardV2Tests.cs
```

Confirm the solution builds and all tests pass before proceeding:
```bash
dotnet build IAW.slnx
dotnet test IAW.slnx
```

---

## PHASE 1: STRUCTURAL CONSISTENCY & DEAD CODE CLEANUP

### 1.1 — Standardize Agent Anatomy

Every agent MUST follow this exact structure. Audit every agent in `src/Agents/` and enforce:

```csharp
public class XxxAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<ModelType>] IChatClient chatClient)
    : Agent(durableState, chatClient), IXxx
{
    // 1. DisplayName (required)
    protected override string DisplayName => "...";

    // 2. Instructions (required, multi-line string)
    protected override string Instructions => """...""";

    // 3. AgentKindValue (only if non-default)
    protected override AgentKind AgentKindValue => AgentKind.Static;

    // 4. GetContextProviders() (if the agent uses context)
    protected override IReadOnlyList<IAgentContextProvider> GetContextProviders() => [...];

    // 5. DefineTools() (if the agent has tools)
    protected override IReadOnlyList<AITool> DefineTools() => [...];

    // 6. Tool method implementations (private, with [Description] attributes)

    // 7. IReceiver<T> implementations (if any)

    // 8. IStreamConsumer<T> implementations (if any)

    // 9. Public interface method implementations
}
```

**Actions:**
- Check each agent file against this template. Reorder members if they're out of order.
- Ensure every agent has a meaningful, specific `Instructions` string — not a vague one-liner. If an agent's Instructions is just "You are a helpful assistant", rewrite it to describe that agent's specific role, capabilities, and behavioral rules.
- Ensure every agent that could benefit from context enrichment implements `GetContextProviders()`. Specifically:
  - `FileSystemAgent` → should use `ProjectContextProvider`
  - `GitAgent` → should use `ProjectContextProvider`
  - `BuildAgent` → should use `ProjectContextProvider`
  - `ReviewerAgent` → should use `ProjectContextProvider` + `MemoryContextProvider` (pattern memory)
  - `PlanningAgent` → should use `MemoryContextProvider` (project memory) + `ProjectContextProvider`
  - `KnowledgeAgent` → should use `MemoryContextProvider` (project + pattern memory) + `RAGContextProvider`
- Remove any `/// <summary>` XML doc comments that slipped in (the codebase convention is no XML docs).

### 1.2 — Eliminate Duplicate Message Types

There are TWO `CodeChangedEvent` types:
- `src/Agents/Messages/CodeChangedEvent.cs`
- `src/Core/Messages/CodeChangedEvent.cs`

And TWO `CodeChangedMessage` types:
- `src/Core/Communication/Messages/CodeChangedMessage.cs`

**Action:**
- Determine which is canonical (the one in `Core.Messages` as an `IEvent` for streaming, and the one in `Core.Communication.Messages` as a P2P message).
- If both are needed, rename to distinguish clearly: `CodeChangedEvent` (stream event) vs `CodeChangedNotification` (P2P message).
- If the one in `src/Agents/Messages/` duplicates `Core.Messages`, delete it and update all references.
- Ensure ArchitectureGuardTests cover this: no duplicate event/message type names across namespaces.

### 1.3 — Audit Unused Interfaces

Check every interface in `src/Agents/` (e.g., `ICodeOrchestrator`, `IDeployer`, `INotificationAgent`, `ITaskSupervisor`, `IPlanning`) and verify:
1. It extends `IAgent`
2. It has at least one implementation
3. It's actually referenced somewhere (either in `ResolveAgent()`, in DI, in another agent, or in tests)

If an interface exists but is never used (no one calls `GrainFactory.GetGrain<IXxx>()`), either wire it up or add a `// TODO: Wire up` comment explaining the intended integration point.

### 1.4 — Clean Up `ResolveAgent` in PersonalAssistantAgent

The `ResolveAgent` method uses two separate resolution strategies:
1. A hardcoded `Dictionary<string, Func<IAgent>>` for base agents
2. A `ResolveAgentByReflection` fallback with a switch expression mapping keys to interface names

**Action:**
- Consolidate into a single, data-driven resolution approach. Create a static dictionary or registry that maps grain keys to interface types:
```csharp
private static readonly Dictionary<string, Type> AgentInterfaces = new(StringComparer.OrdinalIgnoreCase)
{
    ["reviewer"] = typeof(IReviewer),
    ["self-improvement"] = typeof(ISelfImprovement),
    ["deployer"] = typeof(IDeployer),
    ["planning"] = typeof(IPlanning),
    ["roslyn"] = typeof(IRoslyn),
    ["dot-net"] = typeof(IDotNet),
    // ... all agents
};
```
- Remove the reflection-based fallback entirely.
- Make sure the Instructions prompt in PersonalAssistantAgent lists ALL available agents with their keys (currently it lists 14 agents in Instructions but only resolves ~16 in code — ensure they match exactly).

---

## PHASE 2: MEMORY SYSTEM — FROM PLACEHOLDER TO FUNCTIONAL

### 2.1 — Implement Memory Consolidation

`Memory.Consolidate()` is currently `return Task.CompletedTask`. Implement it:

```csharp
protected virtual async Task Consolidate(CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    if (memories.Count < 20) return; // not enough to consolidate

    // Group memories by similarity (cluster nearby embeddings)
    // For each cluster with 3+ entries: summarize into one consolidated entry via LLM
    // Remove originals, add consolidated entry with provenance indicating consolidation
    // Preserve the highest relevance score from the group

    var candidates = new List<(int Index, MemoryEntry Entry)>();
    for (var i = 0; i < memories.Count; i++)
        candidates.Add((i, memories[i]));

    // Find clusters: entries with cosine similarity > 0.85
    var clusters = FindClusters(candidates, 0.85f);

    foreach (var cluster in clusters.Where(c => c.Count >= 3))
    {
        var content = string.Join("\n", cluster.Select(c => c.Entry.Content));
        var prompt = $"Consolidate these related facts into one concise summary:\n{content}";
        try
        {
            var response = await ChatClient.GetResponseAsync(
                [new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, prompt)],
                cancellationToken: ct);
            var consolidated = new MemoryEntry(
                Guid.NewGuid().ToString("N"),
                response.Text ?? content,
                new MemoryProvenance("consolidation", null, this.GetPrimaryKeyString(), null, DateTimeOffset.UtcNow, null, 1.0f),
                cluster.Max(c => c.Entry.RelevanceScore),
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null);

            // Remove originals (reverse order to preserve indices)
            foreach (var idx in cluster.Select(c => c.Index).OrderByDescending(i => i))
                memories.RemoveAt(idx);

            memories.Add(consolidated);
        }
        catch { /* consolidation failure is non-fatal */ }
    }
    await WriteStateAsync(ct);
}
```

Also add the `FindClusters` helper method. Use cosine similarity on embeddings to group nearby entries.

### 2.2 — Wire Up Memory Decay

Add a periodic reminder to each memory agent that calls `Decay()`. In each memory agent's activation:

```csharp
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);
    // Decay memories every 24 hours
    await this.RegisterOrUpdateReminder("memory-decay", TimeSpan.FromHours(1), TimeSpan.FromHours(24));
}

public override async Task ReceiveReminder(string reminderName, TickStatus status)
{
    if (reminderName == "memory-decay")
    {
        await Decay(0.95f, AgentCancellation);
        await Consolidate(AgentCancellation);
        return;
    }
    await base.ReceiveReminder(reminderName, status);
}
```

### 2.3 — Wire All Memory Types into Context

Currently only `PersonalAssistantAgent` uses memory context (UserMemory + ProjectMemory). Wire up the other memory types:

- **EpisodeMemory**: Should be observed by PersonalAssistant after each completed task (store a brief summary of what was done). Add to `ReceiveAsync(TaskCompletedMessage)`:
  ```csharp
  var episodeMemory = GrainFactory.GetGrain<IEpisodeMemory>("episode-memory");
  await episodeMemory.ObserveAsync($"Completed task: {message.Result}", "task-completion", ct);
  ```

- **PatternMemory**: Should be observed by ReviewerAgent when it finds recurring code patterns. Add to the review result handler.

- **CodeMemory**: Should be observed by FileSystemAgent or Roslyn when significant code changes happen.

- Add `EpisodeMemory` and `PatternMemory` to PersonalAssistant's `GetContextProviders()`:
  ```csharp
  new MemoryContextProvider([
      GrainFactory.GetGrain<IUserMemory>("user-memory"),
      GrainFactory.GetGrain<IProjectMemory>("project-memory"),
      GrainFactory.GetGrain<IEpisodeMemory>("episode-memory"),
      GrainFactory.GetGrain<IPatternMemory>("pattern-memory"),
  ])
  ```

### 2.4 — Add Memory Size Guardrails

In `Memory.Observe()`, add a check:
```csharp
// Evict oldest low-relevance memory if at capacity
const int MaxMemories = 500;
if (memories.Count >= MaxMemories)
{
    var lowestIdx = -1;
    var lowestScore = float.MaxValue;
    for (var i = 0; i < memories.Count; i++)
    {
        if (memories[i].RelevanceScore < lowestScore)
        {
            lowestScore = memories[i].RelevanceScore;
            lowestIdx = i;
        }
    }
    if (lowestIdx >= 0)
        memories.RemoveAt(lowestIdx);
}
```

---

## PHASE 3: ORCHESTRATION — FROM SYNCHRONOUS TO PRODUCTION

### 3.1 — Integrate CheckpointStore into ScriptExecutor

`CheckpointStore` exists but is never used. Wire it in:

1. Add `CheckpointStore` as a constructor parameter to `CodeOrchestratorAgent`
2. In the orchestration execution loop, after each successful step, save:
   ```csharp
   await checkpointStore.SaveAsync(taskId, stepIndex, stepResult, ct);
   ```
3. Before executing a plan, check for existing checkpoints:
   ```csharp
   for (var i = 0; i < plan.Steps.Count; i++)
   {
       var existing = await checkpointStore.LoadAsync(taskId, i, ct);
       if (existing is not null)
       {
           // Skip already-completed step, use cached result
           continue;
       }
       // Execute step...
   }
   ```

### 3.2 — Make PersonalAssistant's Task Delegation Non-Blocking

Currently `AssignTaskToAgent` calls `agent.GetResponseStream()` and awaits the entire response inline. This blocks the PersonalAssistant from doing anything else. For long-running tasks, this is a problem.

**Add a fire-and-forget task execution mode:**

```csharp
[Description("Assign a background task to an agent (returns immediately, agent works asynchronously)")]
private async Task<string> AssignBackgroundTask(
    [Description("Grain key of the target agent")] string agentKey,
    [Description("Description of the task")] string description,
    CancellationToken ct = default)
{
    var agent = ResolveAgent(agentKey);
    if (agent is null) return $"Unknown agent key: {agentKey}";

    var taskId = Guid.NewGuid().ToString("N")[..8];
    State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
        JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = "running" }));
    await WriteStateAsync(ct);

    // Fire and forget — agent will publish completion/failure events
    _ = Task.Run(async () =>
    {
        try
        {
            var result = await agent.GetResponse(description, CancellationToken.None);
            State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
                JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = "completed", Result = result }));
            await WriteStateAsync(CancellationToken.None);
            await PublishAsync("task.completed", new Dictionary<string, object>
            {
                ["TaskId"] = taskId, ["AssignedTo"] = agentKey, ["Result"] = result
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            State[$"task-{taskId}"] = new StateEntry($"task-{taskId}",
                JsonSerializer.Serialize(new { Description = description, AssignedTo = agentKey, Status = "failed", Error = ex.Message }));
            await WriteStateAsync(CancellationToken.None);
            await PublishAsync("task.failed", new Dictionary<string, object>
            {
                ["TaskId"] = taskId, ["AssignedTo"] = agentKey, ["Error"] = ex.Message
            }, CancellationToken.None);
        }
    });

    return $"Background task {taskId} assigned to {agentKey}. I'll notify you when it completes.";
}

[Description("Check the status of a previously assigned task")]
private Task<string> CheckTaskStatus(
    [Description("Task ID to check")] string taskId)
{
    var key = $"task-{taskId}";
    if (!State.TryGetValue(key, out var entry))
        return Task.FromResult($"Task {taskId} not found.");
    return Task.FromResult(entry.Value.ToString() ?? "Unknown status");
}
```

Add both tools to `DefineTools()`. Update the Instructions to teach the LLM when to use synchronous `AssignTaskToAgent` vs async `AssignBackgroundTask`:
```
- For quick tasks (file reads, simple commands): use AssignTaskToAgent (waits for result)
- For long tasks (builds, reviews, multi-step plans): use AssignBackgroundTask (returns immediately)
```

### 3.3 — Wire TaskSupervisor to Detect Stalled Tasks

`TaskSupervisorAgent` stores health records but never checks for stalls. Add:

```csharp
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);
    await this.RegisterOrUpdateReminder("stall-check", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
}

public override async Task ReceiveReminder(string reminderName, TickStatus status)
{
    if (reminderName == "stall-check")
    {
        await CheckForStalledTasks(AgentCancellation);
        return;
    }
    await base.ReceiveReminder(reminderName, status);
}

private async Task CheckForStalledTasks(CancellationToken ct)
{
    var stallThreshold = TimeSpan.FromMinutes(10);
    var now = DateTimeOffset.UtcNow;

    foreach (var kvp in State.Where(k => k.Key.StartsWith(TaskPrefix)))
    {
        var record = JsonSerializer.Deserialize<TaskHealthRecord>(kvp.Value.Value.ToString()!);
        if (record is null || record.IsStalled) continue;

        if (now - record.LastProgressAt > stallThreshold)
        {
            var stalled = record with { IsStalled = true, StallReason = $"No progress for {stallThreshold.TotalMinutes} minutes" };
            State[kvp.Key] = new StateEntry(kvp.Key, JsonSerializer.Serialize(stalled));

            await PublishAsync("task.stalled", new Dictionary<string, object>
            {
                ["TaskId"] = record.TaskId,
                ["OrchestratorId"] = record.OrchestratorId,
                ["StallDuration"] = (now - record.LastProgressAt).TotalMinutes
            }, ct);
        }
    }
    await WriteStateAsync(ct);
}
```

### 3.4 — Activate HistorySummarizer

`HistorySummarizer` is created in `Agent.OnActivateAsync()` and passed to `DurableChatHistoryProvider`, but `SummarizeIfNeededAsync()` is never called.

Check `DurableChatHistoryProvider` — if it doesn't call summarization automatically, add a call in `Agent.StreamResponseCore()` after a successful response:

```csharp
// After the streaming loop completes successfully, trigger summarization
if (durableState.History.Count > 40)
{
    // DurableChatHistoryProvider should handle this internally
    // If not, add explicit call here
}
```

Verify that `DurableChatHistoryProvider` actually invokes `SummarizeIfNeededAsync` when history exceeds the threshold. If it doesn't, wire it up.

---

## PHASE 4: ERROR HANDLING & RESILIENCE

### 4.1 — Remove Bare `catch` Blocks

Search the entire codebase for `catch { }` and `catch { // ... }` (bare catches that swallow all exceptions). There are at least 8 instances. For each one:

1. If it's a fire-and-forget helper (like embedding generation), add logging:
   ```csharp
   catch (Exception ex)
   {
       logger.LogWarning(ex, "Non-critical operation failed: {Operation}", "embedding-generation");
   }
   ```
2. If it's in a critical path (like `BuildContextBlock`), at minimum log:
   ```csharp
   catch (Exception ex)
   {
       logger.LogError(ex, "Context provider {Provider} failed for agent {AgentId}", provider.Name, agentId);
   }
   ```
3. Never swallow `OperationCanceledException` — always rethrow or check `ct.IsCancellationRequested` first.

**Files to audit (at minimum):**
- `Agent.cs` lines 226-232 (context provider error)
- `Agent.cs` lines 293-335 (ingestion/attachment errors)
- `Memory.cs` (embedding generation errors — already has logging, good)
- `ScriptExecutor.cs` (process failures)
- `PersonalAssistantAgent.cs` (reflection errors in ResolveAgentByReflection)
- All agents in `src/Agents/` — grep for `catch` and audit each one

### 4.2 — Add Input Validation to ShellAgent

`ShellAgent` executes arbitrary shell commands from LLM output. Add a validation layer:

```csharp
private static readonly string[] BlockedCommands = [
    "rm -rf /", "format", "del /s /q", "mkfs",
    "shutdown", "reboot", "poweroff",
    "curl", "wget", "Invoke-WebRequest", // prevent data exfiltration
    "> /dev/", "| nc ", "| ncat ", // prevent pipes to network
];

private static readonly string[] BlockedPatterns = [
    @"\.\./\.\.",           // path traversal
    @";\s*rm\s",            // chained rm
    @"\|\s*bash",           // pipe to bash
    @"`.*`",                // command substitution
    @"\$\(.*\)",            // command substitution
];

private string? ValidateCommand(string command)
{
    foreach (var blocked in BlockedCommands)
        if (command.Contains(blocked, StringComparison.OrdinalIgnoreCase))
            return $"Command blocked: contains prohibited pattern '{blocked}'";

    foreach (var pattern in BlockedPatterns)
        if (Regex.IsMatch(command, pattern, RegexOptions.IgnoreCase))
            return $"Command blocked: matches security pattern";

    return null; // valid
}
```

Call `ValidateCommand()` at the start of the shell execution tool before running anything. Also add:
- **Output size limit**: Truncate command output to 50KB. If output exceeds this, return a truncated result with a note.
- **Timeout**: Ensure every command has a timeout (the existing 120s default is fine, but make sure it's enforced for ALL execution paths).

### 4.3 — Strengthen Workspace Path Validation

In `Agent.State.cs`, the `ValidatePathWithinWorkspace` method uses `Path.GetFullPath` but doesn't handle:
- Symlinks that escape the workspace
- UNC paths on Windows
- Null/empty paths

Improve it:
```csharp
protected void ValidatePathWithinWorkspace(string path)
{
    if (string.IsNullOrWhiteSpace(path))
        throw new ArgumentException("Path cannot be null or empty.", nameof(path));

    var workspace = GetWorkspacePath();
    if (workspace is null) return;

    var fullPath = Path.GetFullPath(path);
    var fullWorkspace = Path.GetFullPath(workspace);

    // Ensure workspace path ends with separator for proper prefix matching
    if (!fullWorkspace.EndsWith(Path.DirectorySeparatorChar))
        fullWorkspace += Path.DirectorySeparatorChar;

    if (!fullPath.StartsWith(fullWorkspace, StringComparison.OrdinalIgnoreCase)
        && !fullPath.Equals(fullWorkspace.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"Path '{path}' is outside the workspace.");
}
```

---

## PHASE 5: AGENT INSTRUCTIONS QUALITY

This is critical. The assistant's "intelligence" is largely determined by the quality of each agent's Instructions prompt. Audit and rewrite EVERY agent's Instructions to be specific, actionable, and behavior-defining.

### 5.1 — PersonalAssistantAgent Instructions

The current instructions are decent but need refinement. Rewrite to:

```csharp
protected override string Instructions => """
    You are the Personal Assistant — the primary interface between the user and the IAW engineering team.

    CORE BEHAVIOR:
    - You are concise, direct, and action-oriented. Never explain what you're "about to do" — just do it.
    - When the user asks a question you can answer from memory or context, answer directly.
    - When the user asks you to DO something (build, fix, deploy, review, write code), delegate immediately.
    - Always report results, not intentions. Say "Build succeeded with 0 warnings" not "Let me run the build for you."

    DELEGATION RULES:
    - For quick operations (file reads, simple commands, status checks): use AssignTaskToAgent (synchronous, waits for result)
    - For long-running work (full builds, code reviews, multi-step plans, deployments): use AssignBackgroundTask (async, returns immediately)
    - When delegating, give the target agent a clear, specific prompt. Include file paths, expected outcomes, and constraints.
    - If a delegated task fails, try ONE retry with a refined prompt before reporting the failure to the user.

    MEMORY:
    - When the user shares personal facts (name, birthday, preferences, project goals), call RememberFact immediately.
    - When your context includes memories, use them naturally without saying "according to my memory."
    - When the user asks "do you remember...", call RecallMemories and answer based on results.

    YOUR TEAM:
    - roslyn: C# code intelligence (syntax analysis, type info, refactoring suggestions)
    - dot-net: .NET CLI (build, test, format, publish, migrate)
    - reviewer: Code review (quality, patterns, potential bugs)
    - self-improvement: Codebase metrics, quality trends, automated improvements
    - deployer: Git commits, releases, CI/CD
    - planning: Multi-step execution plans for complex tasks
    - knowledge: Project conventions, architecture decisions, documentation
    - nu-get: Package management (search, install, update, vulnerability audit)
    - git-hub: GitHub API (PRs, issues, releases, actions)
    - shell: Shell command execution
    - file-system: File read/write/search/list
    - git: Git operations (status, diff, branch, merge)
    - build: Compilation and test execution
    - aspire: Aspire service orchestration

    CONSTRAINTS:
    - If you say you will delegate, you MUST call AssignTaskToAgent or AssignBackgroundTask in the same turn.
    - Never end a response with a trailing action ("now let me..."). If there's more to do, do it; if not, stop.
    - When multiple tasks are independent, use AssignBackgroundTask for each and report them all.
    """;
```

### 5.2 — TaskSupervisorAgent Instructions

Rewrite from the vague current version to something actionable:

```csharp
protected override string Instructions => """
    You are the Task Supervisor — the IAW team's operational monitor.

    YOUR RESPONSIBILITIES:
    1. Track all active tasks and their progress (completed steps vs total steps)
    2. Detect stalled tasks (no progress for >10 minutes) and escalate
    3. When asked for status, provide a clear health report:
       - Active tasks with progress percentage
       - Any stalled or failed tasks with duration and last known state
       - Recommended actions (retry, escalate, cancel)

    FORMAT:
    - Use brief, structured reports
    - Include task IDs, agent names, and timing information
    - Recommend specific actions, not vague suggestions
    """;
```

### 5.3 — ReviewerAgent Instructions

Make the reviewer specific about what it checks and how it reports:

```csharp
protected override string Instructions => """
    You are the Code Reviewer — the IAW team's quality guardian.

    WHEN REVIEWING CODE:
    1. Check for: correctness, error handling, edge cases, naming, consistency with project patterns
    2. Check for: security issues (SQL injection, path traversal, unvalidated input, hardcoded secrets)
    3. Check for: performance issues (N+1 queries, unbounded loops, missing async/await)
    4. Check for: maintainability (excessive complexity, missing abstractions, code duplication)

    REVIEW FORMAT:
    - Start with a one-line summary verdict: APPROVE, NEEDS_CHANGES, or BLOCK
    - List issues by severity: CRITICAL > HIGH > MEDIUM > LOW
    - Each issue: file path, line range, description, and concrete fix suggestion
    - End with what's GOOD about the code (positive reinforcement)

    CONSTRAINTS:
    - Be specific. "Consider error handling" is bad. "Add try-catch around the HTTP call on line 42 to handle HttpRequestException" is good.
    - Don't flag style preferences. Only flag things that are bugs, risks, or clearly inconsistent with project conventions.
    """;
```

### 5.4 — Rewrite Instructions for ALL Other Agents

Apply the same rigor to every agent. Each Instructions string must:
1. State the agent's exact role in 1-2 sentences
2. List specific capabilities/actions the agent can take
3. Define output format expectations
4. Include constraints and anti-patterns to avoid

Agents to rewrite (at minimum):
- `FileSystemAgent` — specify path validation rules, output format for file listings
- `ShellAgent` — specify command safety rules, output truncation behavior
- `GitAgent` — specify supported operations, branch naming conventions
- `BuildAgent` — specify how to report build results (pass/fail/warnings/errors)
- `KnowledgeAgent` — specify what knowledge it maintains and how it answers queries
- `PlanningAgent` — specify plan format, step granularity, how to handle ambiguity
- `DeployerAgent` — specify deployment safety checks, rollback awareness
- `SelfImprovementAgent` — specify analysis criteria, proposal format, safety constraints
- `NotificationAgent` — specify notification format and routing rules

---

## PHASE 6: CONTEXT PROVIDERS — COMPLETE THE IMPLEMENTATIONS

### 6.1 — ProjectContextProvider

If this is a stub, implement it:
```csharp
public class ProjectContextProvider(IGrainFactory grainFactory) : IAgentContextProvider
{
    public string Name => "Project";

    public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct)
    {
        var items = new List<string>();

        // Try to get project state from the Project grain
        try
        {
            var project = grainFactory.GetGrain<IProject>("default");
            var state = await project.GetState(ct);

            if (state.Entries.TryGetValue("workspace-path", out var workspace))
                items.Add($"[Project workspace: {workspace.Value}]");

            // Add active tasks context
            var activeTasks = state.Entries
                .Where(kvp => kvp.Key.StartsWith("task-") && kvp.Value.Value.ToString()?.Contains("running") == true)
                .Take(5);
            foreach (var task in activeTasks)
                items.Add($"[Active task: {task.Value.Value}]");
        }
        catch { /* project grain not available */ }

        return items;
    }
}
```

### 6.2 — TaskStreamContextProvider

Implement to pull recent orchestration events:
```csharp
public async Task<IReadOnlyList<string>> GetContextAsync(string agentId, string prompt, CancellationToken ct)
{
    // Query recent events from the task stream
    // Provide context about what's currently running, recently completed, or recently failed
}
```

### 6.3 — Context Deduplication

In `Agent.BuildContextBlock()`, add deduplication after collecting all context items:

```csharp
var contextParts = new List<string>();
// ... collect from all providers ...

// Deduplicate exact matches
contextParts = contextParts.Distinct().ToList();

// Optional: deduplicate by similarity (if two items are >90% similar, keep only the first)
```

---

## PHASE 7: TELEGRAM CLIENT HARDENING

### 7.1 — Add Error Recovery to TelegramBotService

Wrap the message handling in proper error recovery:
- If an agent call fails, send a user-friendly error message back to Telegram (not a stack trace)
- If the Orleans cluster is unreachable, queue messages for retry (or inform the user)
- Add a timeout for agent responses (e.g., 5 minutes for regular tasks, 15 minutes for background tasks)

### 7.2 — Add Progress Indicators

For long-running tasks, send a "typing" indicator to Telegram while waiting for the agent:
```csharp
// Send typing action while streaming
await botClient.SendChatActionAsync(chatId, ChatAction.Typing);
```

### 7.3 — Support Structured Responses

When agents return structured data (lists, tables, code), format them appropriately for Telegram:
- Code blocks with ``` syntax
- Bullet points for lists
- Truncate long responses and offer "See full response" via a follow-up button

---

## PHASE 8: TESTING GAPS

### 8.1 — Add Integration Tests for Agent Communication

Add tests that verify:
1. PersonalAssistant can delegate to FileSystem and get a result
2. PersonalAssistant can delegate to Build and handle both success and failure
3. Memory agents can observe, search, and return relevant results
4. Stream events (CodeChangedEvent) reach the ReviewerAgent

### 8.2 — Add Tests for Error Paths

Test what happens when:
1. An agent's LLM call fails (mock ChatClient throws)
2. A delegated agent is unavailable (grain activation fails)
3. Memory embedding generation fails (mock embedder throws)
4. Shell command times out
5. Workspace path validation rejects a malicious path

### 8.3 — Add Memory System Tests

Test:
1. `Observe` + `Search` round-trip (store a fact, search for it, find it)
2. `Decay` reduces relevance scores
3. `Consolidate` merges similar memories (when implemented)
4. `Forget` removes the correct memory
5. Memory cap enforcement (500 limit)

### 8.4 — Update Architecture Guard Tests

Add guards for the new patterns:
```csharp
[Fact]
public void All_agents_must_have_meaningful_Instructions()
{
    // Verify no agent has the default "You are a helpful AI assistant" instructions
    // (except the LLM wrapper agents which inherit from Core.LLM)
}

[Fact]
public void No_bare_catch_blocks_in_agents()
{
    // Scan agent source files for catch blocks that don't log
}

[Fact]
public void No_duplicate_event_type_names()
{
    // Ensure event/message types have unique names across all namespaces
}
```

---

## PHASE 9: OBSERVABILITY & DIAGNOSTICS

### 9.1 — Add Structured Logging

Every agent should have an `ILogger` available. Ensure logging at these points:
- Agent activation/deactivation
- Task delegation (who → whom, what prompt)
- Task completion/failure (duration, result summary)
- Memory operations (observe/search/consolidate with counts)
- Orchestration step progress (step N of M, duration)

Use structured logging with proper templates:
```csharp
logger.LogInformation("Agent {AgentId} delegated task to {TargetAgent}: {TaskDescription}",
    agentId, targetKey, description);
```

### 9.2 — Expose Agent Health via GetMetadata

Ensure `GetMetadata()` returns useful information for diagnostics:
```csharp
public Task<AgentMetadata> GetMetadata(CancellationToken ct = default)
{
    return Task.FromResult(new AgentMetadata(
        DisplayName,
        GetType().Name,
        this.GetPrimaryKeyString(),
        durableState.History.Count,
        durableState.State.Count,
        durableState.EventLog.Count,
        GetWorkspacePath(),
        durableState.TrackingItems.Count));
}
```

---

## PHASE 10: FINAL VERIFICATION

After all phases are complete:

1. **Build**: `dotnet build IAW.slnx` — must succeed with 0 errors, 0 warnings (TreatWarningsAsErrors is on)
2. **Test**: `dotnet test IAW.slnx` — all tests must pass including new ones
3. **Architecture Guards**: Ensure all ArchitectureGuardTests and ArchitectureGuardV2Tests still pass
4. **Review CLAUDE.md**: Update CLAUDE.md to reflect any structural changes made during this cleanup:
   - New agents or tools added
   - Changed communication patterns
   - New context providers
   - Updated testing patterns
5. **Code style**: Verify no `/// <summary>` comments were added, no unnecessary using statements, consistent formatting

Run final verification:
```bash
dotnet build IAW.slnx
dotnet test IAW.slnx
```

---

## EXECUTION ORDER SUMMARY

| Phase | Scope | Risk | Estimated Changes |
|-------|-------|------|-------------------|
| 0 | Read-only orientation | None | 0 files |
| 1 | Structural consistency | Low | ~20 files |
| 2 | Memory system | Medium | ~8 files |
| 3 | Orchestration | Medium-High | ~6 files |
| 4 | Error handling | Low | ~15 files |
| 5 | Agent Instructions | Low | ~15 files |
| 6 | Context providers | Medium | ~5 files |
| 7 | Telegram hardening | Medium | ~3 files |
| 8 | Testing | Low | ~8 new files |
| 9 | Observability | Low | ~10 files |
| 10 | Final verification | None | CLAUDE.md |

**Total: ~90 file touches across 10 phases**

Build and test after EVERY phase. If a phase breaks something, fix it before moving on. Do not skip phases.
