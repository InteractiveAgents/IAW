# CodeOrchestrator Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the CodeOrchestrator so generated orchestration code properly leverages typed agent APIs, uses correct workspace paths, and doesn't fail on token limits or missing usings.

**Architecture:** Seven targeted fixes across 6 files. The system prompt is rewritten to teach agent-first workflows using typed methods (ExecuteAsync, BuildAsync, AnalyzeBuildErrorsAsync) instead of banning agents. Workspace path and selected agents are injected into the prompt. IFileSystem drops its hard sandbox — workspace becomes default context, not a jail. Token limits and finish-reason detection prevent truncation failures.

**Tech Stack:** C#, Orleans, Microsoft.Extensions.AI, xunit.v3

---

### File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Core/Orchestration/ScriptGenerator.cs` | Modify | Add ImplicitUsings to generated .csproj |
| `src/Core/Agents/Agent.State.cs` | Modify | Soften ValidatePathWithinWorkspace — add ResolvePathAgainstWorkspace |
| `src/Agents/Infrastructure/FileSystemAgent.cs` | Modify | Use ResolvePathAgainstWorkspace, update AgentInstructions |
| `src/Agents/Infrastructure/IFileSystem.cs` | Modify | Update AgentInstructions to match new behavior |
| `src/Core/Contracts/ICodeOrchestrator.cs` | Modify | Add selectedAgents param to ExecuteCodeOrchestration |
| `src/Agents/Orchestration/CodeOrchestratorAgent.cs` | Modify | Rewrite system prompt, inject workspace+agents, finish_reason check, bump max_tokens, fix GetResponse override |
| `src/Agents/Orchestration/ThreadAgent.cs` | Modify | Pass selection.SelectedAgents to orchestrator |
| `test/Core.Tests/CodeOrchestratorTests.cs` | Modify | Update tests for new signature |

---

### Task 1: Add ImplicitUsings to orchestration.csproj

**Files:**
- Modify: `src/Core/Orchestration/ScriptGenerator.cs:21-32`

- [ ] **Step 1: Add ImplicitUsings property**

In `ScriptGenerator.GenerateCsproj()`, add `<ImplicitUsings>enable</ImplicitUsings>` to the PropertyGroup:

```csharp
return $"""
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net11.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <DisableMSBuildAssemblyCopyCheck>true</DisableMSBuildAssemblyCopyCheck>
      </PropertyGroup>
      <ItemGroup>
        {refs}
      </ItemGroup>
    </Project>
    """;
```

- [ ] **Step 2: Verify build still works**

Run: `dotnet build src/Core`
Expected: clean build

- [ ] **Step 3: Commit**

```bash
git add src/Core/Orchestration/ScriptGenerator.cs
git commit -m "fix: add ImplicitUsings to orchestration.csproj template"
```

---

### Task 2: Soften workspace validation in Agent.State.cs

**Files:**
- Modify: `src/Core/Agents/Agent.State.cs:28-45`

- [ ] **Step 1: Change ValidatePathWithinWorkspace to not throw**

Replace the method. When workspace is set, it resolves relative paths against workspace. It no longer throws for paths outside workspace — the workspace is a default context, not a boundary:

```csharp
protected string ResolvePathAgainstWorkspace(string path)
{
    if (string.IsNullOrEmpty(path))
        throw new ArgumentException("Path cannot be null or empty.", nameof(path));

    // If path is already absolute, use it as-is
    if (Path.IsPathRooted(path))
        return Path.GetFullPath(path);

    // Relative paths resolve against workspace
    var workspace = GetWorkspacePath();
    return workspace is not null
        ? Path.GetFullPath(Path.Combine(workspace, path))
        : Path.GetFullPath(path);
}
```

Keep `ValidatePathWithinWorkspace` as a deprecated no-op so callers don't break during migration:

```csharp
[Obsolete("Workspace is no longer a hard boundary. Use ResolvePathAgainstWorkspace for path resolution.")]
protected void ValidatePathWithinWorkspace(string path) { }
```

- [ ] **Step 2: Verify Core builds**

Run: `dotnet build src/Core`
Expected: clean build (with obsolete warnings from callers — that's fine)

- [ ] **Step 3: Commit**

```bash
git add src/Core/Agents/Agent.State.cs
git commit -m "refactor: soften workspace validation — default context, not boundary"
```

---

### Task 3: Update FileSystemAgent and IFileSystem.AgentInstructions

**Files:**
- Modify: `src/Agents/Infrastructure/FileSystemAgent.cs`
- Modify: `src/Agents/Infrastructure/IFileSystem.cs:16-38`

- [ ] **Step 1: Replace all ValidatePathWithinWorkspace calls with ResolvePathAgainstWorkspace**

In every method, replace the validation call and use the resolved path:

For `ReadFileAsync`:
```csharp
public async Task<string> ReadFileAsync(string path, CancellationToken ct = default)
{
    var resolvedPath = ResolvePathAgainstWorkspace(path);
    var content = await File.ReadAllTextAsync(resolvedPath, ct);
    // ... rest stays the same but use resolvedPath for metrics
```

Apply same pattern to `WriteFileAsync`, `ListFilesAsync`, `SearchCodeAsync`, `CompareDirectoriesAsync` — replace `ValidatePathWithinWorkspace(x)` with `var resolved = ResolvePathAgainstWorkspace(x)` and use the resolved path for all operations.

- [ ] **Step 2: Update IFileSystem.AgentInstructions to match new behavior**

In `src/Agents/Infrastructure/IFileSystem.cs`, update the RULES section. Replace:
```
RULES:
- ALWAYS validate paths are within the workspace boundary before any operation
- Reject requests for paths outside the workspace explicitly
- Never read or write files outside the workspace
```

With:
```
RULES:
- Relative paths resolve against the workspace directory
- Absolute paths are used as-is — the assistant has full file access
- Workspace is the default working directory, not a security boundary
```

- [ ] **Step 3: Verify build**

Run: `dotnet build src/Agents`
Expected: clean build

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Infrastructure/FileSystemAgent.cs src/Agents/Infrastructure/IFileSystem.cs
git commit -m "refactor: FileSystem uses workspace as default context, not boundary"
```

---

### Task 4: Rewrite CodeOrchestratorAgent + ThreadAgent + Interface (single commit)

This is the main task. All three files that touch the interface change are committed together to avoid broken intermediate builds.

Changes:
1. `ICodeOrchestrator` — add selectedAgents parameter
2. `CodeOrchestratorAgent` — system prompt rewrite, workspace+agents injection, finish_reason check, max_tokens, fix GetResponse override
3. `ThreadAgent` — pass selectedAgents to orchestrator

**Files:**
- Modify: `src/Core/Contracts/ICodeOrchestrator.cs`
- Modify: `src/Agents/Orchestration/CodeOrchestratorAgent.cs`
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`

- [ ] **Step 1: Update ICodeOrchestrator interface**

```csharp
[ResponseTimeout("00:15:00")]
Task<string> ExecuteCodeOrchestration(string plan, IReadOnlyList<string> selectedAgents, CancellationToken ct = default);
```

- [ ] **Step 2: Update ThreadAgent.ExecuteSelection to pass selected agents**

```csharp
var orchestrator = GrainFactory.Get<ICodeOrchestrator>(threadId);
var plan = selection.Plan ?? $"Execute: {request}\nAgents: {string.Join(", ", selection.SelectedAgents)}";
return await orchestrator.ExecuteCodeOrchestration(plan, selection.SelectedAgents, ct);
```

- [ ] **Step 3: Update CodeOrchestratorAgent — ExecuteCodeOrchestration signature and workspace injection**

Update the method to accept `selectedAgents` and pass workspace to instructions:

```csharp
public async Task<string> ExecuteCodeOrchestration(string prompt, IReadOnlyList<string> selectedAgents, CancellationToken ct = default)
{
    try
    {
        var workspacePath = Environment.GetEnvironmentVariable("IAW__Workspace")
            ?? Path.Combine(Path.GetTempPath(), "iaw-workspace");

        _cachedInstructions = BuildInstructions(_cachedAgentCatalog, workspacePath, selectedAgents);

        // ... rest of method unchanged
```

Store the agent catalog separately so it can be reused:

```csharp
string _cachedAgentCatalog = "";
string _cachedInstructions = "";
```

In `OnActivateAsync`, store catalog separately:

```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken)
{
    try
    {
        var registry = GrainFactory.GetGrain<IAgentRegistry>("global");
        _cachedAgentCatalog = await registry.ToPromptStringAsync(cancellationToken);
    }
    catch
    {
        _cachedAgentCatalog = "";
    }
    _cachedInstructions = BuildInstructions(_cachedAgentCatalog, "", []);
    await base.OnActivateAsync(cancellationToken);
}
```

- [ ] **Step 4: Fix the GetResponse override for new signature**

The existing `GetResponse` override calls `ExecuteCodeOrchestration` with only the plan string. Update it to pass an empty agents list (this path is for direct `[EXECUTE_CODE]` invocations without agent selection):

```csharp
public override async Task<string> GetResponse(string prompt, CancellationToken ct = default)
{
    if (prompt.StartsWith("[EXECUTE_CODE]"))
        return await ExecuteCodeOrchestration(prompt["[EXECUTE_CODE]\n".Length..], [], ct);
    return await base.GetResponse(prompt, ct);
}
```

- [ ] **Step 5: Rewrite BuildInstructions with agent-first system prompt**

Replace `BuildInstructions` entirely. The new prompt:
- Teaches typed agent methods as primary tools
- Injects workspace path and selected agents
- Shows correct examples using ExecuteAsync, BuildAsync, AnalyzeBuildErrorsAsync
- Allows File.WriteAllText for content the code generates itself
- Removes "under 80 lines" restriction

```csharp
static string BuildInstructions(string agentCatalog, string workspacePath, IReadOnlyList<string> selectedAgents)
{
    var agentsList = selectedAgents.Count > 0
        ? string.Join(", ", selectedAgents)
        : "any available agents";

    return $$"""
    You generate standalone C# console apps that orchestrate IAW agents. Output ONLY valid C# code. No markdown. No explanation.

    WORKSPACE: {{workspacePath}}
    Create all project artifacts under this path unless the plan specifies a different location.

    SELECTED AGENTS: {{agentsList}}
    Use ONLY these agents. Do not reference agents not in this list.

    TEMPLATE (always start with this exact boilerplate):
    ```
    using System;
    using System.IO;
    using System.Threading;
    using System.Text.Json;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Aspire.IAW;
    using Orleans;
    using Core;
    using Core.Contracts;
    using IAW.Agents.System;
    using IAW.Agents.Coding;

    var builder = Host.CreateApplicationBuilder(args);
    builder.AddIAWClient();
    using var host = builder.Build();
    await host.StartAsync();
    var client = host.Services.GetRequiredService<IClusterClient>();
    var taskId = "task-" + Guid.NewGuid().ToString("N");

    // YOUR CODE HERE

    await host.StopAsync();
    ```

    AGENT API — USE TYPED METHODS (not GetResponse):

    IShell — command execution:
      shell.RunDotnetAsync("new winforms -n MyApp -o /path", "/workdir", default) → CommandResult
      shell.RunDotnetAsync("build", "/projectDir", default) → CommandResult
      shell.ExecuteAsync("npm install", "/dir", 300_000, default) → CommandResult
      CommandResult has: ExitCode (int), Output (string), Error (string), Duration (TimeSpan)
      Use RunDotnetAsync for all dotnet CLI commands. Use ExecuteAsync for other shell commands.

    IDotNet — build and test:
      dotnet.BuildAsync("/path/to/project.csproj", "Debug", default) → BuildRunResult
      dotnet.TestAsync("ClassName.MethodName", default) → TestRunResult
      BuildRunResult has: Success (bool), Output (string), Warnings (int), Errors (int), Duration (TimeSpan), Diagnostics (string[])
      Use build.Diagnostics for error messages (string[]), NOT build.Errors (which is int count).
      TestRunResult has: AllPassed (bool), Total (int), Passed (int), Failed (int), Output (string)

    IRoslyn — code intelligence:
      roslyn.AnalyzeBuildErrorsAsync(buildOutput, default) → string (analysis with fix suggestions)
      roslyn.GetTypeMapAsync(default) → string (all types in workspace)
      roslyn.FindReferencesAsync("MethodName", default) → string
      roslyn.GetWorkspaceStatusAsync(default) → string

    IFileSystem — file operations:
      fs.ReadFileAsync("/path/to/file.cs", default) → string
      await fs.WriteFileAsync("/path/to/file.cs", content, default) → Task (always await)
      fs.ListFilesAsync("/dir", "*.cs", default) → string[]
      fs.SearchCodeAsync("pattern", "/dir", "*.cs", default) → string[]

    IGit — version control:
      git.StatusAsync("/repoPath", default) → string
      git.CommitAsync("/repoPath", "message", default) → string
      git.DiffAsync("/repoPath", default) → string

    WHEN TO USE WHAT:
    - Project scaffolding: shell.RunDotnetAsync("new winforms ...", dir, default)
    - Building: dotnet.BuildAsync(projectPath, default) — returns typed BuildRunResult
    - Fixing build errors: roslyn.AnalyzeBuildErrorsAsync(errors, default)
    - Writing NEW file content you generate: File.WriteAllText() — direct, no agent needed
    - Reading/modifying EXISTING files: fs.ReadFileAsync / fs.WriteFileAsync
    - Running non-dotnet commands: shell.ExecuteAsync(cmd, dir, timeoutMs, default)
    - Do NOT use LLM agents (ISonnet46, IGpt4oMini, etc.) to generate code — YOU write the code.
    - Do NOT use shell.GetResponse() or dotnet.GetResponse() — these waste an LLM roundtrip. Use typed methods.

    COMPLETE EXAMPLE (scaffold a project, modify files, build, verify):
    ```
    var shell = client.Get<IShell>(taskId);
    var dotnet = client.Get<IDotNet>(taskId);
    var roslyn = client.Get<IRoslyn>(taskId);

    // Step 1: Scaffold
    var scaffold = await shell.RunDotnetAsync("new console -n MyApp -o {{workspacePath}}/MyApp", null, default);
    Console.WriteLine("Scaffold exit: " + scaffold.ExitCode);

    // Step 2: Modify generated files
    var programPath = Path.Combine("{{workspacePath}}", "MyApp", "Program.cs");
    File.WriteAllText(programPath, @"Console.WriteLine(""Hello from IAW!"");");

    // Step 3: Build
    var build = await dotnet.BuildAsync("{{workspacePath}}/MyApp/MyApp.csproj", "Debug", default);
    Console.WriteLine("Build success: " + build.Success);

    // Step 4: If errors, analyze with Roslyn
    if (!build.Success)
    {
        var analysis = await roslyn.AnalyzeBuildErrorsAsync(string.Join("\n", build.Diagnostics), default);
        Console.WriteLine("Roslyn analysis: " + analysis);
    }

    // Step 5: Write result
    var resultObj = new Dictionary<string, object>
    {
        ["status"] = build.Success ? "success" : "failed",
        ["summary"] = build.Success ? "Project built successfully" : string.Join("\n", build.Diagnostics),
        ["artifacts"] = new[] { "{{workspacePath}}/MyApp" },
        ["metrics"] = new Dictionary<string, object>()
    };
    File.WriteAllText("result.json", JsonSerializer.Serialize(resultObj));
    ```

    RULES:
    - Get agents: `client.Get<IInterfaceName>(taskId)` — one instance per task
    - Always write result.json with status, summary, artifacts, metrics fields
    - Wrap everything in try/catch, write error result.json in catch
    - Use Dictionary<string, object> for result.json
    - Prefer `dotnet new` templates over hand-writing project files when a template exists

    {{agentCatalog}}
    """;
}
```

- [ ] **Step 6: Update GenerateCode with finish_reason detection and higher max_tokens**

```csharp
const int DefaultMaxTokens = 16384;
const int MaxTokensCap = 32768;

private async Task<string> GenerateCode(string plan, CancellationToken ct)
{
    var maxTokens = DefaultMaxTokens;
    string lastCode = "";

    for (var attempt = 0; attempt < 3; attempt++)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, Instructions),
            new(Microsoft.Extensions.AI.ChatRole.User, plan)
        };
        var options = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = maxTokens };
        var response = await ChatClient.GetResponseAsync(messages, options, ct);
        lastCode = StripMarkdownFences(response.Text ?? "");

        if (response.FinishReason == ChatFinishReason.Length && maxTokens < MaxTokensCap)
        {
            maxTokens = Math.Min(maxTokens * 2, MaxTokensCap);
            continue;
        }

        return lastCode;
    }

    return lastCode;
}
```

- [ ] **Step 7: Update RegenerateCode with same finish_reason handling and max_tokens**

```csharp
private async Task<string> RegenerateCode(string plan, string previousCode, string buildErrors, CancellationToken ct)
{
    var maxTokens = DefaultMaxTokens;
    string lastCode = "";

    for (var attempt = 0; attempt < 3; attempt++)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, Instructions),
            new(Microsoft.Extensions.AI.ChatRole.User, plan),
            new(Microsoft.Extensions.AI.ChatRole.Assistant, previousCode),
            new(Microsoft.Extensions.AI.ChatRole.User,
                $"The code above has build errors. Fix them and output the COMPLETE corrected code.\n\nBuild errors:\n{buildErrors}")
        };
        var options = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = maxTokens };
        var response = await ChatClient.GetResponseAsync(messages, options, ct);
        lastCode = StripMarkdownFences(response.Text ?? "");

        if (response.FinishReason == ChatFinishReason.Length && maxTokens < MaxTokensCap)
        {
            maxTokens = Math.Min(maxTokens * 2, MaxTokensCap);
            continue;
        }

        return lastCode;
    }

    return lastCode;
}
```

- [ ] **Step 8: Extract StripMarkdownFences helper**

```csharp
private static string StripMarkdownFences(string code)
{
    code = code.Trim();
    if (code.StartsWith("```"))
    {
        var firstNewline = code.IndexOf('\n');
        if (firstNewline >= 0) code = code[(firstNewline + 1)..];
    }
    if (code.EndsWith("```"))
        code = code[..^3].TrimEnd();
    return code;
}
```

- [ ] **Step 9: Verify full solution builds**

Run: `dotnet build IAW.slnx`
Expected: clean build

- [ ] **Step 10: Commit all three files together**

```bash
git add src/Core/Contracts/ICodeOrchestrator.cs src/Agents/Orchestration/CodeOrchestratorAgent.cs src/Agents/Orchestration/ThreadAgent.cs
git commit -m "feat: rewrite CodeOrchestrator — agent-first prompt, finish_reason detection, workspace+agents injection"
```

---

### Task 5: Update tests

**Files:**
- Modify: `test/Core.Tests/CodeOrchestratorTests.cs`

- [ ] **Step 1: Update ExecuteCodeOrchestration calls with selectedAgents parameter**

```csharp
[Fact]
public async Task ExecuteCodeOrchestration_CreatesWorkspaceFiles()
{
    var ct = TestContext.Current.CancellationToken;
    var testWorkspace = Path.Combine(Path.GetTempPath(), $"iaw-test-{Guid.NewGuid():N}");
    Environment.SetEnvironmentVariable("IAW__Workspace", testWorkspace);

    try
    {
        var orchestrator = (ICodeOrchestrator)Agent(UniqueId("orch"));
        var result = await orchestrator.ExecuteCodeOrchestration(
            "INTENT: Test. STEPS: 1. Print hello", ["IShell"], ct);

        Assert.NotNull(result);
        Assert.NotEmpty(result);

        var tasksDir = Path.Combine(testWorkspace, "tasks");
        Assert.True(Directory.Exists(tasksDir), $"Tasks dir should exist at {tasksDir}. Result was: {result[..Math.Min(500, result.Length)]}");

        var taskDirs = Directory.GetDirectories(tasksDir);
        Assert.Single(taskDirs);

        var taskDir = taskDirs[0];
        Assert.True(File.Exists(Path.Combine(taskDir, "plan.md")), "plan.md should exist");
        Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.cs")), "orchestration.cs should exist");
        Assert.True(File.Exists(Path.Combine(taskDir, "orchestration.csproj")), "orchestration.csproj should exist");
        Assert.True(File.Exists(Path.Combine(taskDir, "log.txt")), "log.txt should exist");

        Assert.Contains("Workspace:", result);
    }
    finally
    {
        Environment.SetEnvironmentVariable("IAW__Workspace", null);
        if (Directory.Exists(testWorkspace))
            Directory.Delete(testWorkspace, recursive: true);
    }
}

[Fact]
public async Task ExecuteCodeOrchestration_ReturnsErrorOnBadPath()
{
    var ct = TestContext.Current.CancellationToken;
    Environment.SetEnvironmentVariable("IAW__Workspace", "Z:\\nonexistent\\path");

    try
    {
        var orchestrator = (ICodeOrchestrator)Agent(UniqueId("orch-err"));
        var result = await orchestrator.ExecuteCodeOrchestration("test plan", ["IShell"], ct);

        Assert.Contains("CodeOrchestrator error:", result);
    }
    finally
    {
        Environment.SetEnvironmentVariable("IAW__Workspace", null);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~CodeOrchestratorTests"`
Expected: all 3 tests pass

- [ ] **Step 3: Commit**

```bash
git add test/Core.Tests/CodeOrchestratorTests.cs
git commit -m "test: update CodeOrchestrator tests for new selectedAgents param"
```

---

### Task 6: Full build and test verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: clean build, zero errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: all tests pass

- [ ] **Step 3: Final commit if any fixups needed**

```bash
git add -A
git commit -m "fix: address build/test issues from CodeOrchestrator refactor"
```
