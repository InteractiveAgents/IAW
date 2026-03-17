using System.Diagnostics;
using System.Text;
using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.Orchestration;
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
        2. Use top-level statements
        3. Use `builder.AddIAWClient()` from Aspire.IAW.Client to connect to the Orleans cluster
        4. Call agent grain interfaces (IAgent.GetResponse, IAgent.GetResponseStream) for AI tasks
        5. Write a result.json file at the end with: status, summary, artifacts array, metrics object
        6. Write any output files to an "output" subdirectory
        7. Wrap the main logic in try/catch and report errors to result.json
        8. Print progress to stdout (it will be captured and streamed to the user)

        Available agent interfaces (all implement IAgent with GetResponse/GetResponseStream):
        - IFileSystem (file-system): read, write, search, list files
        - IShell (shell): execute shell commands
        - IBuild (build): compile and test .NET projects
        - IGit (git): version control operations
        - IReviewer (reviewer): code quality review

        Output ONLY the C# code. No markdown, no explanation. Just the code.
        """;

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

            var code = await GenerateCode(prompt, ct);
            var codePath = Path.Combine(taskDir, "orchestration.cs");
            await File.WriteAllTextAsync(codePath, code, ct);

            var csprojContent = GenerateCsproj();
            await File.WriteAllTextAsync(Path.Combine(taskDir, "orchestration.csproj"), csprojContent, ct);

            var (exitCode, log) = await ExecuteProject(taskDir, ct);
            await File.WriteAllTextAsync(Path.Combine(taskDir, "log.txt"), log, ct);

            if (exitCode != 0)
            {
                var errorSummary = log.Length > 2000 ? log[^2000..] : log;
                return $"Code execution failed (exit code {exitCode}).\nWorkspace: {taskDir}\nLast output:\n{errorSummary}";
            }

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

    private async Task<string> GenerateCode(string plan, CancellationToken ct)
    {
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(Microsoft.Extensions.AI.ChatRole.System, Instructions),
            new(Microsoft.Extensions.AI.ChatRole.User, plan)
        };
        var response = await ChatClient.GetResponseAsync(messages, cancellationToken: ct);
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
