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
    IChatClient chatClient)
    : Agent<ICodeOrchestrator>(durableState, chatClient), ICodeOrchestrator
{
    static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(10);

    string _cachedInstructions = "";

    protected override string Instructions => _cachedInstructions.Length > 0 ? _cachedInstructions : BuildFallbackInstructions();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var registry = GrainFactory.GetGrain<IAgentRegistry>("global");
            var catalogPrompt = await registry.ToPromptStringAsync(cancellationToken);
            _cachedInstructions = BuildInstructions(catalogPrompt);
        }
        catch
        {
            _cachedInstructions = BuildInstructions("");
        }
        await base.OnActivateAsync(cancellationToken);
    }

    static string BuildFallbackInstructions() => BuildInstructions("");

    static string BuildInstructions(string agentCatalog)
    {
        return $$"""
        You generate standalone C# console apps. Output ONLY valid C# code. No markdown. No explanation.

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

        CRITICAL — RETURN TYPES:
        - `agent.GetResponse("prompt", default)` returns `string` — plain text, NOT a structured object.
        - Do NOT call .Summary, .Status, .Content, or ANY property on the result. It is a string.
        - Use the string directly: `var result = await agent.GetResponse("...", default);`
        - To include it in result.json, use it as-is: `summary = result`
        - Specialized methods like `IDotNet.BuildAsync()` return typed results (e.g., `BuildRunResult`).

        COMPLETE EXAMPLE (create a project with files and build it):
        ```
        // WRITE FILES DIRECTLY — do NOT use agents to generate file content.
        // You already know what code to write, so write it with File.WriteAllText.
        Directory.CreateDirectory("D:/MyApp");

        File.WriteAllText("D:/MyApp/MyApp.csproj", @"<Project Sdk=""Microsoft.NET.Sdk"">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net11.0</TargetFramework>
          </PropertyGroup>
        </Project>");

        File.WriteAllText("D:/MyApp/Program.cs", @"Console.WriteLine(""Hello World"");");

        // Use IShell or IDotNet only for EXECUTING commands (build, test, run)
        var shell = client.Get<IShell>(taskId);
        var buildResult = await shell.GetResponse("cd D:/MyApp && dotnet build", default);
        Console.WriteLine(buildResult);

        var resultObj = new Dictionary<string, object> { ["status"] = "success", ["summary"] = buildResult, ["artifacts"] = new[] { "D:/MyApp" }, ["metrics"] = new Dictionary<string, object>() };
        File.WriteAllText("result.json", JsonSerializer.Serialize(resultObj));
        ```

        CRITICAL — FILE WRITING:
        - Write files DIRECTLY with `File.WriteAllText()` and `Directory.CreateDirectory()`.
        - Do NOT use IFileSystem, IShell, or any agent to write file content. You have full disk access.
        - Do NOT use LLM agents (ISonnet46, IGpt4oMini, etc.) to generate code. YOU generate the code directly.
        - Use agents ONLY for: building (IDotNet.BuildAsync), running tests (IDotNet.TestAsync),
          executing shell commands (IShell), git operations (IGit), and analysis (IRoslyn).

        RULES:
        - Get agents via `client.Get<IInterfaceName>(taskId)` — isolated instances per task.
        - `GetResponse()` returns `string`. Always. Never call properties on it.
        - For parallel work use `await Task.WhenAll(task1, task2)`.
        - Always write result.json with status, summary, artifacts, and metrics fields.
        - Keep code SHORT. Under 80 lines. No unnecessary abstractions.
        - Wrap everything in try/catch, write error to result.json in catch.
        - For result.json use Dictionary: `new Dictionary<string, object> { ["status"] = "success", ["summary"] = result }`

        {{agentCatalog}}
        """;
    }

    protected override IReadOnlyList<AITool> DefineTools() => [];

    public override async Task<string> GetResponse(string prompt, CancellationToken ct = default)
    {
        if (prompt.StartsWith("[EXECUTE_CODE]"))
            return await ExecuteCodeOrchestration(prompt["[EXECUTE_CODE]\n".Length..], ct);
        return await base.GetResponse(prompt, ct);
    }

    public async Task<string> ExecuteCodeOrchestration(string prompt, CancellationToken ct = default)
    {
        try
        {
            var workspacePath = Environment.GetEnvironmentVariable("IAW__Workspace")
                ?? Path.Combine(Path.GetTempPath(), "iaw-workspace");

            var slug = GenerateSlug(prompt);
            var taskId = $"{DateTime.UtcNow:yyyy-MM-dd}-{slug}-{Guid.NewGuid().ToString("N")[..6]}";
            var taskDir = Path.Combine(workspacePath, "tasks", taskId);
            Directory.CreateDirectory(taskDir);
            Directory.CreateDirectory(Path.Combine(taskDir, "output"));

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
                var buildErrors = await TryBuild(taskDir, ct);
                if (buildErrors is null) break; // clean build

                if (attempt == maxRetries)
                {
                    await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), buildErrors, ct);
                    return $"Code generation failed after {maxRetries + 1} attempts.\nWorkspace: {taskDir}\nBuild errors:\n{buildErrors}";
                }

                code = await RegenerateCode(prompt, code, buildErrors, ct);
                await File.WriteAllTextAsync(codePath, code, ct);
            }

            var (exitCode, log) = await ExecuteProject(taskDir, ct);
            await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), log, ct);

            if (exitCode != 0)
            {
                var errorSummary = log.Length > 2000 ? log[^2000..] : log;
                return $"Code execution failed (exit code {exitCode}).\nWorkspace: {taskDir}\nLast output:\n{errorSummary}";
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
                return $"Completed. Workspace: {taskDir}\nResult: {resultJson}";
            }

            var lastOutput = log.Length > 1000 ? log[^1000..] : log;
            return $"Completed (no result.json). Workspace: {taskDir}\nOutput:\n{lastOutput}";
        }
        catch (Exception ex)
        {
            return $"CodeOrchestrator error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
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
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, Instructions),
            new(Microsoft.Extensions.AI.ChatRole.User, plan),
            new(Microsoft.Extensions.AI.ChatRole.Assistant, previousCode),
            new(Microsoft.Extensions.AI.ChatRole.User,
                $"The code above has build errors. Fix them and output the COMPLETE corrected code.\n\nBuild errors:\n{buildErrors}\n\nREMEMBER: GetResponse() returns string, not a structured object. Do NOT call .Summary, .Status, or any property on it.")
        };
        var options = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = 4096 };
        var response = await ChatClient.GetResponseAsync(messages, options, ct);
        var code = (response.Text ?? "").Trim();
        if (code.StartsWith("```"))
        {
            var firstNewline = code.IndexOf('\n');
            code = code[(firstNewline + 1)..];
        }
        if (code.EndsWith("```"))
            code = code[..^3].TrimEnd();
        return code;
    }

    private async Task<string> GenerateCode(string plan, CancellationToken ct)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, Instructions),
            new(Microsoft.Extensions.AI.ChatRole.User, plan)
        };
        var options = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = 4096 };
        var response = await ChatClient.GetResponseAsync(messages, options, ct);
        var code = (response.Text ?? "").Trim();
        if (code.StartsWith("```"))
        {
            var firstNewline = code.IndexOf('\n');
            code = code[(firstNewline + 1)..];
        }
        if (code.EndsWith("```"))
            code = code[..^3].TrimEnd();
        return code;
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
}
