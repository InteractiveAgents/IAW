using System.Diagnostics;
using System.Text;
using Core;
using Core.AI;
using Core.AI.Models;
using Core.Communication.Messages;
using Core.Contracts;
using Core.Orchestration;
using Core.Registry;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Orchestration;

[GrainType(IAWConstants.GrainTypes.CodeOrchestrator)]
public class CodeOrchestratorAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Opus46>] IChatClient chatClient)
    : Agent<ICodeOrchestrator>(durableState, chatClient), ICodeOrchestrator
{
    static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(10);

    string _cachedAgentCatalog = "";
    string _cachedInstructions = "";

    protected override string Instructions => _cachedInstructions.Length > 0 ? _cachedInstructions : BuildFallbackInstructions();

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

    static string BuildFallbackInstructions() => BuildInstructions("", "", []);

    static string BuildInstructions(string agentCatalog, string workspacePath, IReadOnlyList<string> selectedAgents)
    {
        var agentsList = selectedAgents.Count > 0
            ? string.Join(", ", selectedAgents)
            : "any available agents";

        return $$"""
        You generate standalone C# console apps that orchestrate IAW agents. Output ONLY valid C# code. No markdown. No explanation.

        WORKSPACE: {{workspacePath}}
        The input contains USER REQUEST (what was asked) and PLAN (how to do it).
        If the user request specifies a path (e.g. "at D:\IAW\Calc"), use THAT path — not the workspace.
        Only fall back to the workspace if no specific path is mentioned.

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
        - ALWAYS use `dotnet new` templates via shell.RunDotnetAsync instead of hand-writing .csproj and boilerplate files
        - Target framework is net11.0 (or net11.0-windows for WinForms/WPF) unless the user specifies otherwise

        {{agentCatalog}}
        """;
    }

    protected override IReadOnlyList<AITool> DefineTools() => [];

    public override async Task<string> GetResponse(string prompt, CancellationToken ct = default)
    {
        if (prompt.StartsWith("[EXECUTE_CODE]"))
        {
            var result = await ExecuteCodeOrchestration(prompt["[EXECUTE_CODE]\n".Length..], [], "", ct);
            return result.Success
                ? $"Completed. Workspace: {result.WorkspacePath}\nSummary: {result.Summary}"
                : $"Failed. {result.ErrorDetail ?? result.Summary}";
        }
        return await base.GetResponse(prompt, ct);
    }

    public async Task<OrchestrationResult> ExecuteCodeOrchestration(string prompt, IReadOnlyList<string> selectedAgents, string projectKey, CancellationToken ct = default)
    {
        try
        {
            var workspacePath = Environment.GetEnvironmentVariable("IAW__Workspace")
                ?? Path.Combine(Path.GetTempPath(), "iaw-workspace");

            _cachedInstructions = BuildInstructions(_cachedAgentCatalog, workspacePath, selectedAgents);

            var slug = GenerateSlug(prompt);
            var taskId = $"{DateTime.UtcNow:yyyy-MM-dd}-{slug}-{Guid.NewGuid().ToString("N")[..6]}";
            var taskDir = Path.Combine(workspacePath, "tasks", taskId);
            Directory.CreateDirectory(taskDir);
            Directory.CreateDirectory(Path.Combine(taskDir, "output"));

            await PublishProgress(projectKey, taskId, "planning", "Generating orchestration code...", ct);

            await File.WriteAllTextAsync(Path.Combine(taskDir, "plan.md"), prompt, ct);

            var csprojContent = GenerateCsproj();
            await File.WriteAllTextAsync(Path.Combine(taskDir, "orchestration.csproj"), csprojContent, ct);

            var code = await GenerateCode(prompt, ct);
            var codePath = Path.Combine(taskDir, "orchestration.cs");
            await File.WriteAllTextAsync(codePath, code, ct);

            // compile-retry loop: build first, if errors feed them back to LLM
            const int maxRetries = 2;
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                await PublishProgress(projectKey, taskId, "building", $"Building (attempt {attempt + 1})...", ct);
                var buildErrors = await TryBuild(taskDir, ct);
                if (buildErrors is null) break; // clean build

                if (attempt == maxRetries)
                {
                    await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), buildErrors, ct);
                    return new OrchestrationResult(false, $"Code generation failed after {maxRetries + 1} attempts", taskDir, [], null, buildErrors, taskId);
                }

                await PublishProgress(projectKey, taskId, "retrying", $"Fixing build errors (attempt {attempt + 1})...", ct);
                code = await RegenerateCode(prompt, code, buildErrors, ct);
                await File.WriteAllTextAsync(codePath, code, ct);
            }

            await PublishProgress(projectKey, taskId, "executing", "Running orchestration...", ct);
            var (exitCode, log) = await ExecuteProject(taskDir, ct);
            await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), log, ct);

            if (exitCode != 0)
            {
                var errorSummary = log.Length > 2000 ? log[^2000..] : log;
                return new OrchestrationResult(false, $"Code execution failed (exit code {exitCode})", taskDir, [], null, errorSummary, taskId);
            }

            await PublishToStream(new CodeChangedMessage(
                taskDir, "", $"Code orchestration completed for task {taskId}")
            {
                FilePaths = Directory.GetFiles(taskDir, "*.cs", SearchOption.AllDirectories).ToList(),
                SourceAgentId = this.GetPrimaryKeyString()
            }, ct);

            var resultPath = Path.Combine(taskDir, "result.json");
            if (File.Exists(resultPath))
            {
                var resultJson = await File.ReadAllTextAsync(resultPath, ct);
                var parsed = ParseResultJson(resultJson);
                return new OrchestrationResult(
                    parsed.GetValueOrDefault("status")?.ToString() != "failed",
                    parsed.GetValueOrDefault("summary")?.ToString() ?? "Completed",
                    taskDir,
                    ParseArtifacts(parsed),
                    ParseMetrics(parsed),
                    null,
                    taskId);
            }

            var lastOutput = log.Length > 1000 ? log[^1000..] : log;
            return new OrchestrationResult(true, lastOutput, taskDir, [], null, null, taskId);
        }
        catch (Exception ex)
        {
            return new OrchestrationResult(false, $"CodeOrchestrator error: {ex.GetType().Name}", "", [], null, $"{ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task<string?> TryBuild(string taskDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{taskDir}\"",
            WorkingDirectory = taskDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode == 0) return null; // clean build

        var fullOutput = output + error;
        // extract just the error lines
        var errorLines = fullOutput.Split('\n')
            .Where(l => l.Contains(": error "))
            .Take(15)
            .ToList();

        return errorLines.Count > 0
            ? string.Join("\n", errorLines)
            : (fullOutput.Length > 2000 ? fullOutput[^2000..] : fullOutput);
    }

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

    private static string GenerateCsproj() => ScriptGenerator.GenerateCsproj();

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

        var keepVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Orleans__ClusterId", "Orleans__ServiceId", "Orleans__EnableDistributedTracing" };
        foreach (var key in Environment.GetEnvironmentVariables().Keys.Cast<string>()
            .Where(k => k.StartsWith("Orleans__", StringComparison.OrdinalIgnoreCase) && !keepVars.Contains(k)))
        {
            psi.Environment.Remove(key);
        }

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

    private static Dictionary<string, object?> ParseResultJson(string json)
    {
        try { return global::System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ?? []; }
        catch { return []; }
    }

    private static List<string> ParseArtifacts(Dictionary<string, object?> parsed)
    {
        if (!parsed.TryGetValue("artifacts", out var val) || val is not global::System.Text.Json.JsonElement el) return [];
        if (el.ValueKind != global::System.Text.Json.JsonValueKind.Array) return [];
        return [.. el.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0)];
    }

    private static Dictionary<string, string>? ParseMetrics(Dictionary<string, object?> parsed)
    {
        if (!parsed.TryGetValue("metrics", out var val) || val is not global::System.Text.Json.JsonElement el) return null;
        if (el.ValueKind != global::System.Text.Json.JsonValueKind.Object) return null;
        var dict = new Dictionary<string, string>();
        foreach (var prop in el.EnumerateObject())
            dict[prop.Name] = prop.Value.ToString();
        return dict.Count > 0 ? dict : null;
    }

    private async Task PublishProgress(string projectKey, string taskId, string phase, string message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectKey)) return;
        await PublishAsync(IAWConstants.Events.OrchestrationProgress, new Dictionary<string, string>
        {
            [IAWConstants.PayloadKeys.ProjectKey] = projectKey,
            [IAWConstants.PayloadKeys.TaskId] = taskId,
            [IAWConstants.PayloadKeys.Phase] = phase,
            [IAWConstants.PayloadKeys.Message] = message
        }, ct);
    }
}
