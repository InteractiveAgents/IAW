# Agent Team v2 — Phase 1: Team Redesign & Prompt Engineering

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign agent responsibilities so each agent owns its domain completely with proper tooling, three-layer instructions, and engineered tool descriptions. Remove workspace restriction.

**Architecture:** DotNet becomes the full .NET agent (build, run, test, publish, new, list projects). Shell scoped to raw CLI only. Aspire gets resource management + trace reading + log cleanup tools. All agents get three-layer instructions and engineered `[Description]` attributes. `.WithWorkspace()` removed from AppHost.

**Tech Stack:** Orleans grains, Microsoft.Extensions.AI, C# 13, xunit.v3, Aspire MCP

**Spec:** `docs/superpowers/specs/2026-03-23-agent-team-v2-design.md`

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Agents.CSharp/DotNet/IDotNet.cs` | Modify | Add Run, Publish, New, ListProjects methods + engineered descriptions |
| `src/Agents.CSharp/DotNet/DotNetAgent.cs` | Modify | Implement new methods, all with auto-discovery + timeout |
| `src/Agents/Infrastructure/IShell.cs` | Modify | Update instructions to exclude .NET operations |
| `src/Agents/Infrastructure/IAspire.cs` | Modify | Add resource management + trace + log cleanup methods |
| `src/Agents/Infrastructure/AspireAgent.cs` | Modify | Implement new Aspire tools |
| `src/Agents/Infrastructure/IGit.cs` | Modify | Add engineered `[Description]` to all methods |
| `src/Agents/Infrastructure/IFileSystem.cs` | Modify | Add engineered `[Description]` to all methods |
| `src/Agents/Orchestration/IThread.cs` | Modify | Update routing instructions per new agent responsibilities |
| `src/IAW.AppHost/AppHost.cs` | Modify | Remove `.WithWorkspace()` |
| `test/Core.Tests/DotNetAgentTests.cs` | Create | Tests for new DotNet methods |

---

### Task 1: Remove workspace restriction from AppHost

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs:15`

- [ ] **Step 1: Remove .WithWorkspace line**

In `AppHost.cs`, remove `.WithWorkspace("D:\\IAW-Workspace")` (line 15). The line currently reads:
```csharp
    .WithWorkspace("D:\\IAW-Workspace");
```

Change to just the semicolon on the previous line. The full chain becomes:
```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Gpt54Mini>().AsBalanced()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Gpt54Nano>().AsFast()
    .WithLLM<Sonnet46>()
    .WithLLM<Opus46>().AsReasoning()
    .WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text<WhisperLargeV3Turbo>();
```

- [ ] **Step 2: Build**

Run: `dotnet build src/IAW.AppHost`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat: remove workspace restriction — agents have full PC access"
```

---

### Task 2: Add engineered `[Description]` attributes to all agent interfaces

**Files:**
- Modify: `src/Agents.CSharp/DotNet/IDotNet.cs` (lines 21-23)
- Modify: `src/Agents/Infrastructure/IShell.cs` (lines 39-41)
- Modify: `src/Agents/Infrastructure/IGit.cs` (lines 40-45)
- Modify: `src/Agents/Infrastructure/IFileSystem.cs` (lines 40-45)

Every interface method that auto-registers as a tool gets a `[Description]` following the pattern:
`"{verb} {object}. {when to use}. Returns {output shape}."`

- [ ] **Step 1: Engineer IDotNet descriptions**

In `src/Agents.CSharp/DotNet/IDotNet.cs`, add `using System.ComponentModel;` and update method signatures:

```csharp
[Description("Build a .NET project or solution. Accepts a directory path, .csproj, or .sln — auto-discovers project files from directories. Returns success/failure with error count, warning count, duration, and diagnostics.")]
Task<BuildRunResult> BuildAsync(string projectPath, string configuration = "Debug", CancellationToken ct = default);

[Description("Run .NET tests for the workspace project. Optionally filter by test name. Returns pass/fail counts and output.")]
Task<TestRunResult> TestAsync(string? filter = null, CancellationToken ct = default);

[Description("Format C# code in the workspace using dotnet format with editorconfig. Returns summary of changed files.")]
Task<string> FormatAsync(CancellationToken ct = default);
```

- [ ] **Step 2: Engineer IShell descriptions**

In `src/Agents/Infrastructure/IShell.cs`, add `using System.ComponentModel;` and update:

```csharp
[Description("Execute a shell command (cmd.exe on Windows, bash on Linux). 120-second timeout by default. Returns exit code, stdout, stderr, and duration.")]
Task<CommandResult> ExecuteAsync(string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default);

[Description("Run a dotnet CLI command. 120-second timeout with process kill on timeout. Returns exit code, stdout, stderr, and duration.")]
Task<CommandResult> RunDotnetAsync(string arguments, string? workingDirectory = null, CancellationToken ct = default);

[Description("Get shell execution metrics: total commands, failed commands, command frequency, average execution time.")]
Task<ShellMetrics> GetMetricsAsync(CancellationToken ct = default);
```

- [ ] **Step 3: Engineer IGit descriptions**

In `src/Agents/Infrastructure/IGit.cs`, add `using System.ComponentModel;` and update:

```csharp
[Description("Show git status of a repository. Returns branch name, staged/unstaged/untracked files.")]
Task<string> StatusAsync(string repoPath, CancellationToken ct = default);

[Description("Create a git commit with a message. Stage files first with shell if needed. Returns commit hash and message.")]
Task<string> CommitAsync(string repoPath, string message, CancellationToken ct = default);

[Description("Show git diff of unstaged changes in a repository. Returns file paths and line changes.")]
Task<string> DiffAsync(string repoPath, CancellationToken ct = default);

[Description("Show git log of recent commits. Returns hash, author, subject per commit. Default 10 entries.")]
Task<string[]> LogAsync(string repoPath, int count = 10, CancellationToken ct = default);

[Description("Revert a specific git commit by hash. Returns result message.")]
Task<string> RevertAsync(string repoPath, string commitHash, CancellationToken ct = default);
```

- [ ] **Step 4: Engineer IFileSystem descriptions**

In `src/Agents/Infrastructure/IFileSystem.cs`, add `using System.ComponentModel;` and update:

```csharp
[Description("Read a file's contents. Accepts any absolute path on the PC. Truncates to 50KB for large files.")]
Task<string> ReadFileAsync(string path, CancellationToken ct = default);

[Description("Write content to a file. Creates the file and parent directories if they don't exist. Accepts any absolute path.")]
Task WriteFileAsync(string path, string content, CancellationToken ct = default);

[Description("List files in a directory matching a glob pattern. Default pattern '*' lists all files. Returns array of file paths.")]
Task<string[]> ListFilesAsync(string directory, string pattern = "*", CancellationToken ct = default);

[Description("Search for a regex pattern across files in a directory. Returns matching lines as 'file:line: content'. Filter by file extension with fileFilter.")]
Task<string[]> SearchCodeAsync(string pattern, string directory, string fileFilter = "*.cs", CancellationToken ct = default);
```

- [ ] **Step 5: Build all projects**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 6: Run tests**

Run: `dotnet test test/Core.Tests -v minimal`
Expected: All existing tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Agents.CSharp/DotNet/IDotNet.cs src/Agents/Infrastructure/IShell.cs src/Agents/Infrastructure/IGit.cs src/Agents/Infrastructure/IFileSystem.cs
git commit -m "feat: add engineered [Description] attributes to all agent interface methods"
```

---

### Task 3: Update all agent instructions to three-layer pattern

**Files:**
- Modify: `src/Agents.CSharp/DotNet/IDotNet.cs` (lines 17-19)
- Modify: `src/Agents/Infrastructure/IShell.cs` (lines 15-37)
- Modify: `src/Agents/Infrastructure/IGit.cs` (lines 15-38)
- Modify: `src/Agents/Infrastructure/IFileSystem.cs` (lines 16-38)

- [ ] **Step 1: Update IDotNet instructions**

Replace `AgentInstructions` in `IDotNet.cs`:

```csharp
static string IAgent.AgentInstructions => """
    You are DotNet, the .NET toolchain specialist. You build, run, test, publish,
    and scaffold .NET projects. Execute operations immediately and report results.

    RULES:
    - ALWAYS call the appropriate tool — never respond with manual instructions.
    - When given a directory path, Build auto-discovers .csproj/.sln files.
    - For build errors, return the full diagnostic output.
    - For test failures, return pass/fail counts and failing test names.
    - DO NOT execute raw shell commands — use your typed Build/Test/Format tools.
    - DO NOT ask the user for project paths — discover them from the directory.

    TOOLS: Build, Test, Format (auto-registered from interface).
    """;
```

- [ ] **Step 2: Update IShell instructions**

Replace `AgentInstructions` in `IShell.cs`:

```csharp
static string IAgent.AgentInstructions => """
    You are Shell, the command execution specialist. You run CLI commands,
    scripts, and non-.NET tools with timeout enforcement.

    RULES:
    - Execute commands immediately — never tell the user to run them manually.
    - Default 120-second timeout. Kill processes that exceed it.
    - Report: exit code, stdout, stderr, duration.
    - Truncate output to 8KB. Note when truncation occurs.
    - Validate commands — reject dangerous patterns (rm -rf /, format c:, shutdown).
    - DO NOT run 'dotnet build', 'dotnet test', 'dotnet run' — the DotNet agent handles those.
    - Use RunDotnet only for dotnet CLI commands not covered by DotNet agent (e.g., dotnet tool install).

    TOOLS: ExecuteCommand (shell), RunDotnet (dotnet CLI), RunShell (shell).
    """;
```

- [ ] **Step 3: Update IGit instructions**

Replace `AgentInstructions` in `IGit.cs`:

```csharp
static string IAgent.AgentInstructions => """
    You are Git, the version control specialist. You manage commits, branches,
    diffs, and repository state.

    RULES:
    - Execute git operations immediately — never give manual instructions.
    - Always run Status before Commit to verify staged changes.
    - Write commit messages in imperative mood, max 72 characters for subject.
    - Never force-push or rewrite public history.
    - For merge conflicts, report conflicting files and let the user decide.
    - DO NOT modify file contents — use FileSystem agent for that.

    TOOLS: Status, Commit, Diff, Log, Revert.
    """;
```

- [ ] **Step 4: Update IFileSystem instructions**

Replace `AgentInstructions` in `IFileSystem.cs`:

```csharp
static string IAgent.AgentInstructions => """
    You are FileSystem, the file operations specialist. You read, write, list,
    and search files anywhere on the PC.

    RULES:
    - Execute file operations immediately — never give manual instructions.
    - Absolute paths work as-is. Relative paths resolve against workspace if set.
    - No path restrictions — you have full access to the entire filesystem.
    - Truncate file contents to 50KB when reading large files. Note truncation.
    - When writing, auto-create parent directories.
    - DO NOT analyze code — use Roslyn for that. DO NOT build — use DotNet.

    TOOLS: ReadFile, WriteFile, ListFiles, SearchCode, CompareDirectories.
    """;
```

- [ ] **Step 5: Build and test**

Run: `dotnet build IAW.slnx && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Agents.CSharp/DotNet/IDotNet.cs src/Agents/Infrastructure/IShell.cs src/Agents/Infrastructure/IGit.cs src/Agents/Infrastructure/IFileSystem.cs
git commit -m "feat: update all agent instructions to three-layer pattern with negative examples"
```

---

### Task 4: Add Run, ListProjects to DotNet agent

**Files:**
- Modify: `src/Agents.CSharp/DotNet/IDotNet.cs`
- Modify: `src/Agents.CSharp/DotNet/DotNetAgent.cs`

- [ ] **Step 1: Add RunAsync and ListProjectsAsync to IDotNet interface**

Add after the `FormatAsync` method in `IDotNet.cs`:

```csharp
[Description("Run a .NET project with 'dotnet run'. Accepts directory or .csproj path — auto-discovers project. 120-second timeout, kills process on timeout. Returns exit code, stdout, stderr.")]
Task<CommandResult> RunAsync(string projectPath, string? arguments = null, CancellationToken ct = default);

[Description("List all .csproj and .sln files in a directory tree. Use this to discover projects before building. Returns array of absolute file paths.")]
Task<string[]> ListProjectsAsync(string directory, CancellationToken ct = default);
```

Note: `CommandResult` is from `IAW.Agents.System` — add `using IAW.Agents.System;` to IDotNet.cs.

- [ ] **Step 2: Implement RunAsync in DotNetAgent**

Add after the `FormatAsync` method in `DotNetAgent.cs`:

```csharp
public async Task<CommandResult> RunAsync(
    string projectPath, string? arguments = null, CancellationToken ct = default)
{
    var resolvedPath = ResolveProjectPath(projectPath);
    var sw = Stopwatch.StartNew();

    var args = $"run --project \"{resolvedPath}\"";
    if (!string.IsNullOrEmpty(arguments))
        args += $" -- {arguments}";

    var psi = new ProcessStartInfo("dotnet", args)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    if (process is null)
    {
        sw.Stop();
        return new CommandResult(-1, "", "Failed to start dotnet run", sw.Elapsed);
    }

    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(120_000);

    try
    {
        var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);
        sw.Stop();
        return new CommandResult(process.ExitCode, output, error, sw.Elapsed);
    }
    catch (OperationCanceledException)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        sw.Stop();
        return new CommandResult(-1, "", "dotnet run timed out after 120s", sw.Elapsed);
    }
}
```

- [ ] **Step 3: Implement ListProjectsAsync**

Add after `RunAsync`:

```csharp
public Task<string[]> ListProjectsAsync(string directory, CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    if (!Directory.Exists(directory))
        return Task.FromResult(Array.Empty<string>());

    var projects = Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(directory, "*.sln", SearchOption.AllDirectories))
        .Concat(Directory.GetFiles(directory, "*.slnx", SearchOption.AllDirectories))
        .OrderBy(p => p)
        .ToArray();

    return Task.FromResult(projects);
}
```

- [ ] **Step 4: Build and test**

Run: `dotnet build src/Agents.CSharp && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Agents.CSharp/DotNet/IDotNet.cs src/Agents.CSharp/DotNet/DotNetAgent.cs
git commit -m "feat: add RunAsync and ListProjectsAsync to DotNet agent"
```

---

### Task 5: Add resource management and log cleanup tools to Aspire agent

**Files:**
- Modify: `src/Agents/Infrastructure/IAspire.cs`
- Modify: `src/Agents/Infrastructure/AspireAgent.cs`

The current Aspire agent connects to the Aspire CLI MCP server and exposes its tools. We need to add dedicated methods for resource restart and log cleanup that Thread can call directly via SendToAgent.

- [ ] **Step 1: Add methods to IAspire interface**

Add to `IAspire.cs` after the existing interface body (currently a marker interface with only static metadata):

```csharp
[Description("Restart an Aspire resource by name. Stops and starts the resource. Use this to deploy code changes. Common resources: assistant, telegram, devui, mcp.")]
Task<string> RestartResourceAsync(string resourceName, CancellationToken ct = default);

[Description("List all Aspire resources with their current state (Running, Stopped, etc).")]
Task<string> ListResourcesAsync(CancellationToken ct = default);

[Description("Get recent distributed traces for a resource. Shows operation name, duration, and token usage if available.")]
Task<string> GetTracesAsync(string resourceName, CancellationToken ct = default);

[Description("Get recent structured logs for a resource. Filters to errors and warnings by default.")]
Task<string> GetLogsAsync(string resourceName, CancellationToken ct = default);
```

- [ ] **Step 2: Implement methods in AspireAgent**

These methods delegate to the MCP tools the agent already connects to. Add to `AspireAgent.cs` after the `ResolveAppHostPath` method:

```csharp
public async Task<string> RestartResourceAsync(string resourceName, CancellationToken ct = default)
{
    if (_mcpClient is null) return "Aspire MCP not connected";

    try
    {
        await _mcpClient.CallToolAsync("execute_resource_command",
            new Dictionary<string, object> { ["resourceName"] = resourceName, ["commandName"] = "resource-stop" }, ct);
        await Task.Delay(3000, ct);
        await _mcpClient.CallToolAsync("execute_resource_command",
            new Dictionary<string, object> { ["resourceName"] = resourceName, ["commandName"] = "resource-start" }, ct);
        return $"Resource '{resourceName}' restarted successfully";
    }
    catch (Exception ex)
    {
        return $"Failed to restart {resourceName}: {ex.Message}";
    }
}

public async Task<string> ListResourcesAsync(CancellationToken ct = default)
{
    if (_mcpClient is null) return "Aspire MCP not connected";
    var result = await _mcpClient.CallToolAsync("list_resources", new Dictionary<string, object>(), ct);
    return result?.Content.FirstOrDefault()?.Text ?? "No resources found";
}

public async Task<string> GetTracesAsync(string resourceName, CancellationToken ct = default)
{
    if (_mcpClient is null) return "Aspire MCP not connected";
    var result = await _mcpClient.CallToolAsync("list_traces",
        new Dictionary<string, object> { ["resourceName"] = resourceName }, ct);
    return result?.Content.FirstOrDefault()?.Text ?? "No traces found";
}

public async Task<string> GetLogsAsync(string resourceName, CancellationToken ct = default)
{
    if (_mcpClient is null) return "Aspire MCP not connected";
    var result = await _mcpClient.CallToolAsync("list_structured_logs",
        new Dictionary<string, object> { ["resourceName"] = resourceName }, ct);
    return result?.Content.FirstOrDefault()?.Text ?? "No logs found";
}
```

- [ ] **Step 3: Update IAspire instructions**

Replace the `AgentInstructions` in `IAspire.cs`:

```csharp
static string IAgent.AgentInstructions => """
    You are Aspire, the infrastructure and deployment operator. You manage the IAW
    distributed system through the Aspire dashboard.

    RULES:
    - When asked to "deploy" or "apply changes": call RestartResource("assistant").
    - When asked about system health: call ListResources and report states.
    - When asked about performance: call GetTraces and summarize token usage and timing.
    - For debugging: call GetLogs and surface errors/warnings first.
    - DO NOT execute shell commands for infrastructure tasks — use your typed tools.
    - DO NOT restart resources without being asked — deployments need explicit intent.

    TOOLS: RestartResource, ListResources, GetTraces, GetLogs (typed interface methods).
    Additional MCP tools available for deeper queries.
    """;
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Agents`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Infrastructure/IAspire.cs src/Agents/Infrastructure/AspireAgent.cs
git commit -m "feat: add resource management, trace reading, and log tools to Aspire agent"
```

---

### Task 6: Update Thread routing instructions for new agent responsibilities

**Files:**
- Modify: `src/Agents/Orchestration/IThread.cs`

- [ ] **Step 1: Update IThread AgentInstructions**

Replace the full `AgentInstructions` in `IThread.cs`:

```csharp
static string IAgent.AgentInstructions => """
    You are an AI assistant in the IAW (Interactive Agents Workspace) system —
    a multi-agent platform built on Orleans with specialized agents.

    ROUTING RULES:
    - Answer directly: greetings, general knowledge, conversation context
    - SendToAgent for single-agent tasks:
      • "DotNet" — build, run, test, publish .NET projects. Discovers project files automatically.
      • "Shell" — npm, pip, cargo, scripts, non-.NET CLI commands only.
      • "FileSystem" — read/write/list/search files anywhere on the PC.
      • "Git" — status, commit, diff, log, branch, revert.
      • "Roslyn" — analyze C# code, type maps, compilation error diagnostics.
      • "Aspire" — restart services, read traces/logs, check system health, deploy changes.
      • "GitHub" — PRs, issues, repository operations.
    - Orchestrate ONLY for complex tasks needing 3+ agents coordinated together
      (scaffolding + building + testing, multi-file refactoring, code generation pipelines)

    CRITICAL RULES:
    - DO NOT use Orchestrate for tasks that one agent can handle alone.
    - DO NOT route .NET build/run/test to Shell — ALWAYS use DotNet.
    - DO NOT tell the user to run commands manually — agents execute everything.
    - For "fix yourself" / "improve" requests: use FileSystem to read code, Roslyn to
      analyze, FileSystem to write fixes, DotNet to build/test, Aspire to deploy.
    - ALWAYS preserve exact paths from the user's message.
    - Be concise and direct. Use markdown formatting.
    """;
```

- [ ] **Step 2: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests --filter "FullyQualifiedName~Thread" -v minimal`
Expected: 0 errors, all Thread tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/IThread.cs
git commit -m "feat: update Thread routing for new agent responsibilities — DotNet owns builds, Aspire owns deployment"
```

---

### Task 7: End-to-end integration test

**Files:** None (manual testing)

- [ ] **Step 1: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test test/Core.Tests -v minimal`
Expected: All pass (except known pre-existing CodeValidator failures).

- [ ] **Step 3: Restart Aspire assistant**

Use Aspire MCP: restart the `assistant` resource to pick up all changes.

- [ ] **Step 4: Test DotNet build routing**

Send to Thread: `Build the project at D:\Demo\Calc`
Expected: Thread → SendToAgent("DotNet") → DotNet calls Build tool → succeeds.
Verify in traces: DotNet agent activated, Build tool called, no Shell or CodeOrchestrator.

- [ ] **Step 5: Test DotNet run routing**

Send to Thread: `Run the app at D:\Demo\Calc`
Expected: Thread → SendToAgent("DotNet") → DotNet calls Run tool → timeout after 120s (GUI app).
Verify: process killed cleanly, result returned.

- [ ] **Step 6: Test file reading**

Send to Thread: `Read the file D:\Demo\Calc\Program.cs`
Expected: Thread → SendToAgent("FileSystem") → returns file contents. Fast (<5s).

- [ ] **Step 7: Test git status**

Send to Thread: `Show git status of E:\IAW`
Expected: Thread → SendToAgent("Git") → returns branch + changed files. Fast (<5s).

- [ ] **Step 8: Test Aspire health check**

Send to Thread: `Check the system health`
Expected: Thread → SendToAgent("Aspire") → returns resource states.

- [ ] **Step 9: Compare traces — verify token efficiency**

Check Aspire traces for all tests:
- Simple tasks (build, read, git): 2 LLM calls (Thread + agent), <5K total tokens
- No AgentSelector or CodeOrchestrator involvement
- DotNet uses Sonnet 4.6, FileSystem/Git use gpt-5.4-nano (Fast)

- [ ] **Step 10: Commit any fixes found during testing**

If issues found, fix and commit individually.
