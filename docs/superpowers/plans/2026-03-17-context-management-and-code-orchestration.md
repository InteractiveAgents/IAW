# Context Management & Code Orchestration Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent 200k token crashes via tiered context management (L1/L2/L3) and add a CodeOrchestrator agent that generates standalone C# files for complex tasks.

**Architecture:** Two subsystems: (1) Context management — Haiku summarization of tool results, token-aware ChatReducer with post-task compaction, Recall tool backed by Qdrant; (2) Code orchestration — CodeOrchestrator grain generates C# files, executes them out-of-process via `dotnet run`, workspace configured via `.WithWorkspace(path)` in Aspire.

**Tech Stack:** C# / .NET 11, Orleans 10, Qdrant, Microsoft.Extensions.AI (Haiku for summarization), System.Diagnostics.Process, Aspire hosting

**Spec:** `docs/superpowers/specs/2026-03-17-context-management-and-code-orchestration-design.md`

---

## Chunk 1: Context Management — ChatReducer & Summarization

### Task 1: Token-aware safety net in ChatReducer

**Files:**
- Modify: `src/Core/Agents/ChatReducer.cs`

- [ ] **Step 1: Add token estimation constants and TruncateMessage helper**

Add at the top of the `ChatReducer` class:

```csharp
const int MaxMessageChars = 8000;
const int MaxTotalChars = 400_000;
```

Add `TruncateMessage` static method after `EvictImages`:

```csharp
static ChatMessage TruncateMessage(ChatMessage message)
{
    var text = message.Text;
    if (text.Length <= MaxMessageChars) return message;

    var keepEach = MaxMessageChars / 2 - 50;
    var truncated = string.Concat(
        text.AsSpan(0, keepEach),
        "\n\n[...truncated...]\n\n",
        text.AsSpan(text.Length - keepEach));
    return message with
    {
        Content = truncated,
        Parts = [new TextContent(truncated)]
    };
}
```

- [ ] **Step 2: Apply truncation and token budget enforcement in Reduce**

Update the `Reduce` method. After the existing logic that builds the `result` list, apply truncation to all messages and enforce the total token budget:

Replace the current method body with:

```csharp
public IReadOnlyList<ChatMessage> Reduce(
    IReadOnlyList<ChatMessage> fullHistory,
    ChatMessage? summary,
    int recentWindow = 20)
{
    var result = new List<ChatMessage>();

    if (summary is not null)
        result.Add(TruncateMessage(summary));

    var recentStart = Math.Max(0, fullHistory.Count - recentWindow);

    for (var i = 0; i < recentStart; i++)
    {
        if (IsNonReducible(fullHistory[i]))
            result.Add(TruncateMessage(EvictImages(fullHistory[i])));
    }

    for (var i = recentStart; i < fullHistory.Count; i++)
        result.Add(TruncateMessage(fullHistory[i]));

    // token budget enforcement — drop oldest non-summary messages
    var totalChars = result.Sum(m => m.Text.Length);
    while (totalChars > MaxTotalChars && result.Count > 2)
    {
        var removed = result[1];
        result.RemoveAt(1);
        totalChars -= removed.Text.Length;
    }

    return result;
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Core`
Expected: Build succeeded

- [ ] **Step 4: Run existing tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Core/Agents/ChatReducer.cs
git commit -m "feat: add token-aware safety net and message truncation to ChatReducer"
```

### Task 2: Haiku summarization of tool results in PersonalAssistantAgent

**Files:**
- Modify: `src/Agents/Orchestration/PersonalAssistantAgent.cs`

- [ ] **Step 1: Add a SummarizeResult helper method**

Add this method at the end of the `PersonalAssistantAgent` class (before `ResolveAgent`):

```csharp
private async Task<string> SummarizeResult(string fullResult, string agentKey, string description)
{
    if (fullResult.Length < 2000) return fullResult;

    try
    {
        var haikuClient = ServiceProvider.GetKeyedService<IChatClient>("claude-haiku-4-5");
        if (haikuClient is null) return TruncateResult(fullResult, 2000);

        var prompt = $"Summarize this agent result concisely. Preserve key outcomes, numbers, file paths, and errors.\n\nAgent: {agentKey}\nTask: {description}\n\nResult:\n{TruncateResult(fullResult, 6000)}";
        var response = await haikuClient.GetResponseAsync(
            [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, prompt)]);
        return response.Text ?? TruncateResult(fullResult, 2000);
    }
    catch
    {
        return TruncateResult(fullResult, 2000);
    }
}
```

Add `using Microsoft.Extensions.DependencyInjection;` to the imports if not already present.

- [ ] **Step 2: Update AssignTaskToAgent to summarize before returning**

In `AssignTaskToAgent`, replace the final return lines (the part after the catch block that builds the return string). Change:

```csharp
var result = responseBuilder.Length > 0 ? responseBuilder.ToString() : "[Agent acknowledged]";
if (sawError)
    return $"Task assigned to {agentKey} (ID: {taskId}), but the delegated agent reported an error: {result}";

return $"Task assigned to {agentKey} (ID: {taskId}). Response: {result}";
```

To:

```csharp
var fullResult = responseBuilder.Length > 0 ? responseBuilder.ToString() : "[Agent acknowledged]";
var result = await SummarizeResult(fullResult, agentKey, description);
if (sawError)
    return $"Task assigned to {agentKey} (ID: {taskId}), but the delegated agent reported an error: {result}";

return $"Task assigned to {agentKey} (ID: {taskId}). Response: {result}";
```

The full output was already streamed to the user via `WriteToolProgress`. Only the summary enters the orchestrator's LLM context.

- [ ] **Step 3: Build**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Orchestration/PersonalAssistantAgent.cs
git commit -m "feat: Haiku summarization of tool results in AssignTaskToAgent"
```

### Task 3: TaskResultContextProvider (L3 retrieval)

**Files:**
- Create: `src/Core/Context/TaskResultContextProvider.cs`

- [ ] **Step 1: Create the provider**

```csharp
using Microsoft.Extensions.AI;
using Qdrant.Client;

namespace Core.Context;

public class TaskResultContextProvider(
    QdrantClient qdrantClient,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    string userId) : IAgentContextProvider
{
    public string Name => "task-results";

    public async Task<IReadOnlyList<string>> GetContextAsync(
        string agentId, string prompt, CancellationToken ct = default)
    {
        var collectionName = $"task-results-{userId}";

        try
        {
            if (!await qdrantClient.CollectionExistsAsync(collectionName, ct))
                return [];

            var embeddings = await embeddingGenerator.GenerateAsync([prompt], cancellationToken: ct);
            var queryVector = embeddings[0].Vector.ToArray();
            var results = await qdrantClient.SearchAsync(
                collectionName, queryVector, limit: 3, cancellationToken: ct);

            return [.. results
                .Where(r => r.Score > 0.5f)
                .Select(r => $"[past task result] {r.Payload["text"]}")];
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Core`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Core/Context/TaskResultContextProvider.cs
git commit -m "feat: add TaskResultContextProvider for L3 past task result retrieval"
```

### Task 4: Add Recall tool and TaskResultContextProvider to Project grain

**Files:**
- Modify: `src/Agents/Projects/Project.cs`

- [ ] **Step 1: Register TaskResultContextProvider in GetContextProviders**

In the `GetContextProviders` method, after the RAG provider registration (after line 76), add:

```csharp
if (qdrant is not null && embeddings is not null)
{
    var userId = this.GetPrimaryKeyString().Split('/')[0];
    providers.Add(new TaskResultContextProvider(qdrant, embeddings, userId));
}
```

- [ ] **Step 2: Add Recall tool to DefineTools**

In `DefineTools`, add a new entry to the return array:

```csharp
AIFunctionFactory.Create(RecallTool, nameof(RecallTool),
    "Search past task results, conversations, and documents for relevant context"),
```

- [ ] **Step 3: Implement RecallTool method**

Add after `DelegateToAssistant`:

```csharp
[Description("Search past task results, conversations, and documents")]
private async Task<string> RecallTool(
    [Description("What to search for")] string query,
    [Description("Maximum results to return")] int maxResults = 5)
{
    var qdrant = ServiceProvider.GetService<QdrantClient>();
    var embeddings = ServiceProvider.GetService<IEmbeddingGenerator<string, Embedding<float>>>();
    if (qdrant is null || embeddings is null) return "Search not available.";

    var userId = this.GetPrimaryKeyString().Split('/')[0];
    var collections = new[] { $"task-results-{userId}", $"project-{this.GetPrimaryKeyString().Replace("/", "-")}" };
    var results = new List<string>();

    var queryEmbedding = await embeddings.GenerateAsync([query]);
    var queryVector = queryEmbedding[0].Vector.ToArray();

    foreach (var collection in collections)
    {
        try
        {
            if (!await qdrant.CollectionExistsAsync(collection))
                continue;
            var hits = await qdrant.SearchAsync(collection, queryVector, limit: (ulong)maxResults);
            results.AddRange(hits.Where(h => h.Score > 0.4f)
                .Select(h => $"[{collection}] {h.Payload["text"]}"));
        }
        catch { }
    }

    if (results.Count == 0) return "No relevant results found.";
    return string.Join("\n\n", results.Take(maxResults));
}
```

- [ ] **Step 4: Update Project Instructions to mention Recall and ExecuteWithCode**

In the `Instructions` property, update the default case (`_`) and other relevant cases to mention `Recall` and `ExecuteWithCode`. For the default case:

```csharp
_ => """
    You are a project assistant. Help the user manage their project,
    answer questions, and coordinate tasks. Be concise and actionable.

    ROUTING:
    - For simple tasks (build, review, git, single-agent work) — use DelegateToAssistant
    - For complex tasks (loops, data processing, multi-source research, file generation) — use ExecuteWithCode
    - To find past work results or documents — use Recall
    - You cannot create files or run commands yourself.
    """
```

Update the `"general"`, `"iaw"`, and `"personal"` cases similarly to mention the three tools.

- [ ] **Step 5: Build**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 6: Run tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 7: Commit**

```bash
git add src/Agents/Projects/Project.cs
git commit -m "feat: add Recall tool and TaskResultContextProvider to Project grain"
```

---

## Chunk 2: Code Orchestration — CodeOrchestrator Agent

### Task 5: ICodeOrchestrator interface

**Files:**
- Create: `src/Core/Contracts/ICodeOrchestrator.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace Core.Contracts;

public interface ICodeOrchestrator : IAgent;
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Core`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Core/Contracts/ICodeOrchestrator.cs
git commit -m "feat: add ICodeOrchestrator interface"
```

### Task 6: CodeOrchestratorAgent implementation

**Files:**
- Create: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`

- [ ] **Step 1: Create the agent**

```csharp
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace IAW.Agents.Orchestration;

[GrainType("code-orchestrator-v1")]
public class CodeOrchestratorAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Sonnet46>] IChatClient chatClient)
    : Agent(durableState, chatClient), ICodeOrchestrator
{
    static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(10);

    protected override string DisplayName => "Code Orchestrator";

    protected override string Instructions => """
        You are a code orchestrator. You receive a task plan and generate a standalone C# console application
        that executes the plan by calling IAW agent interfaces via the Aspire.IAW.Client package.

        The generated code must:
        1. Be a complete, compilable Program.cs for a .NET console app
        2. Use `builder.AddIAWClient()` to connect to the Orleans cluster (env vars are inherited)
        3. Call agent grain interfaces (IAgent.GetResponse, IAgent.GetResponseStream) for AI tasks
        4. Write a result.json file at the end with: status, summary, artifacts array, metrics object
        5. Write any output files to an "output" subdirectory
        6. Wrap the main logic in try/catch and report errors to result.json
        7. Print progress to stdout (it will be captured and streamed to the user)

        Available agent interfaces (all implement IAgent with GetResponse/GetResponseStream):
        - IFileSystem (file-system): read, write, search, list files
        - IShell (shell): execute shell commands
        - IBuild (build): compile and test .NET projects
        - IGit (git): version control operations
        - IReviewer (reviewer): code quality review
        - INotificationAgent (notification): send alerts

        Output ONLY the C# code. No markdown, no explanation. Just the code.
        """;

    protected override IReadOnlyList<AITool> DefineTools() => [];

    public override async Task<string> GetResponse(string prompt, CancellationToken ct = default)
    {
        var workspacePath = Environment.GetEnvironmentVariable("IAW__Workspace")
            ?? Path.Combine(Path.GetTempPath(), "iaw-workspace");

        var slug = GenerateSlug(prompt);
        var taskId = $"{DateTime.UtcNow:yyyy-MM-dd}-{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        var taskDir = Path.Combine(workspacePath, "tasks", taskId);
        Directory.CreateDirectory(taskDir);
        Directory.CreateDirectory(Path.Combine(taskDir, "output"));

        WriteToolProgress($"Task: {taskId}\n");

        // save the plan
        await File.WriteAllTextAsync(Path.Combine(taskDir, "plan.md"), prompt, ct);

        // generate code
        WriteToolProgress("Generating code...\n");
        var code = await GenerateCode(prompt, ct);
        var codePath = Path.Combine(taskDir, "orchestration.cs");
        await File.WriteAllTextAsync(codePath, code, ct);
        WriteToolProgress($"Code written to {codePath}\n");

        // write csproj from template
        var csprojContent = GenerateCsproj();
        await File.WriteAllTextAsync(Path.Combine(taskDir, "orchestration.csproj"), csprojContent, ct);

        // execute
        WriteToolProgress("Compiling and executing...\n");
        var (exitCode, log) = await ExecuteProject(taskDir, ct);
        await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), log, ct);

        if (exitCode != 0)
        {
            WriteToolProgress($"\nExecution failed (exit code {exitCode})\n");
            var errorSummary = log.Length > 2000 ? log[^2000..] : log;
            return $"Code execution failed (exit code {exitCode}). Last output:\n{errorSummary}";
        }

        // read result.json
        var resultPath = Path.Combine(taskDir, "result.json");
        if (File.Exists(resultPath))
        {
            var resultJson = await File.ReadAllTextAsync(resultPath, ct);
            WriteToolProgress($"\nCompleted. Result: {resultJson}\n");
            return resultJson;
        }

        WriteToolProgress("\nCompleted (no result.json written).\n");
        var lastOutput = log.Length > 1000 ? log[^1000..] : log;
        return $"Execution completed but no result.json was written. Output:\n{lastOutput}";
    }

    private async Task<string> GenerateCode(string plan, CancellationToken ct)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in GetResponseStream(plan, ct))
        {
            sb.Append(chunk);
        }
        // strip markdown code fences if present
        var code = sb.ToString().Trim();
        if (code.StartsWith("```"))
        {
            var firstNewline = code.IndexOf('\n');
            code = code[(firstNewline + 1)..];
        }
        if (code.EndsWith("```"))
            code = code[..^3].TrimEnd();
        return code;
    }

    private static string GenerateCsproj() => """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net11.0</TargetFramework>
            <RootNamespace>Orchestration</RootNamespace>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Aspire.IAW.Client" Version="*" />
          </ItemGroup>
        </Project>
        """;

    private async Task<(int ExitCode, string Log)> ExecuteProject(string taskDir, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ExecutionTimeout);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{taskDir}\"",
            WorkingDirectory = taskDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var log = new StringBuilder();

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            log.AppendLine(e.Data);
            WriteToolProgress(e.Data + "\n");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            log.AppendLine($"[stderr] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cts.Token);
            return (process.ExitCode, log.ToString());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, log + "\n[Killed: execution timed out]");
        }
    }

    private static string GenerateSlug(string plan)
    {
        var words = plan.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(4)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant())
            .Where(w => w.Length > 0);
        var slug = string.Join("-", words);
        return slug.Length > 30 ? slug[..30] : slug;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/CodeOrchestratorAgent.cs
git commit -m "feat: add CodeOrchestratorAgent with code generation and out-of-process execution"
```

### Task 7: Add ExecuteWithCode tool to Project grain

**Files:**
- Modify: `src/Agents/Projects/Project.cs`

- [ ] **Step 1: Add ExecuteWithCode to DefineTools**

In `DefineTools`, add a new entry to the array (after the DelegateToAssistant entry):

```csharp
AIFunctionFactory.Create(ExecuteWithCode, nameof(ExecuteWithCode),
    "Execute a complex task via generated C# code. Use for tasks involving loops, data processing, multi-source research, file generation, or multi-step workflows."),
```

- [ ] **Step 2: Implement ExecuteWithCode method**

Add after `DelegateToAssistant`:

```csharp
[Description("Execute a complex task via generated C# code. " +
    "Provide: what the user wants, success metrics, and step-by-step plan.")]
private async Task<string> ExecuteWithCode(
    [Description("Full plan: intent, success metrics, and steps")] string plan)
{
    var orchestrator = GrainFactory.GetGrain<ICodeOrchestrator>("code-orchestrator");
    var sb = new StringBuilder();
    WriteToolProgress("\n\n---\nGenerating and executing code...\n\n");
    await foreach (var chunk in orchestrator.GetResponseStream(plan, CancellationToken.None))
    {
        sb.Append(chunk);
        WriteToolProgress(chunk);
    }
    WriteToolProgress("\n---\n");
    return sb.ToString();
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Agents`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Projects/Project.cs
git commit -m "feat: add ExecuteWithCode tool to Project grain for code orchestration"
```

---

## Chunk 3: Aspire Integration

### Task 8: Add .WithWorkspace() to IAWService

**Files:**
- Modify: `src/Aspire.Hosting.IAW/IAWService.cs`
- Modify: `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs`

- [ ] **Step 1: Add WorkspacePath property to IAWService**

In `IAWService.cs`, add after the `InfrastructureApplied` property:

```csharp
internal string? WorkspacePath { get; set; }
```

- [ ] **Step 2: Add WithWorkspace extension method**

In `IAWHostingExtensions.cs`, add after `WithVectorDb`:

```csharp
public static IAWService WithWorkspace(this IAWService iaw, string path)
{
    iaw.WorkspacePath = path;
    return iaw;
}
```

- [ ] **Step 3: Propagate workspace env var in WithReference**

In the `WithReference<T>(this IResourceBuilder<T> builder, IAWService iaw)` method, add before the `return builder;` at the end (around line 153):

```csharp
if (!string.IsNullOrEmpty(iaw.WorkspacePath))
    builder.WithEnvironment("IAW__Workspace", iaw.WorkspacePath);
```

Also add the same line in the `WithReference<T>(this IResourceBuilder<T> builder, IAWClientService client)` method (around line 168):

```csharp
if (!string.IsNullOrEmpty(client.IAW.WorkspacePath))
    builder.WithEnvironment("IAW__Workspace", client.IAW.WorkspacePath);
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Aspire.Hosting.IAW`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Aspire.Hosting.IAW/IAWService.cs src/Aspire.Hosting.IAW/IAWHostingExtensions.cs
git commit -m "feat: add .WithWorkspace(path) to IAWService for CodeOrchestrator workspace"
```

### Task 9: Configure workspace in AppHost

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs`

- [ ] **Step 1: Read the file and add .WithWorkspace()**

Find where `AddIAW` is called and chain `.WithWorkspace("D:\\IAW-Workspace")` (or an appropriate default path). The exact location depends on the current AppHost structure.

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/IAW.AppHost`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat: configure IAW workspace path in AppHost"
```

---

## Chunk 4: Build, Test & Verify

### Task 10: Full build and test

- [ ] **Step 1: Full solution build**

Run: `dotnet build IAW.slnx` (or individual projects if file locks)
Expected: Build succeeded with 0 C# errors

- [ ] **Step 2: Run all unit tests**

Run: `dotnet test test/Core.Tests --verbosity normal`
Expected: All tests pass

- [ ] **Step 3: Start Aspire and verify**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`
Verify: All resources start, `IAW__Workspace` env var visible on assistant resource

- [ ] **Step 4: Manual test — send complex task in Telegram**

Send a complex task that should trigger Mode 2. Verify:
- CodeOrchestrator generates C# code
- Code is written to workspace directory
- Execution output streams to Telegram
- Result summary returned

- [ ] **Step 5: Manual test — verify Mode 1 summarization**

Send a simple delegation task. Verify:
- Sub-agent output streams to user
- The return value to the Project LLM is a compact summary, not the full output

- [ ] **Step 6: Commit any fixups**

```bash
git add -A
git commit -m "fix: address build/test issues from context management and code orchestration"
```

---

## Parallelization Guide

**Wave 1** (independent):
- Task 1 (ChatReducer token safety)
- Task 3 (TaskResultContextProvider)
- Task 5 (ICodeOrchestrator interface)
- Task 8 (Aspire WithWorkspace)

**Wave 2** (depends on Wave 1):
- Task 2 (Haiku summarization) — independent
- Task 6 (CodeOrchestratorAgent) — depends on Task 5
- Task 4 (Recall tool + Project updates) — depends on Task 3

**Wave 3** (depends on Wave 2):
- Task 7 (ExecuteWithCode on Project) — depends on Task 6
- Task 9 (AppHost config) — depends on Task 8

**Wave 4** (final):
- Task 10 (Build & verify) — depends on all
