using System.Diagnostics;
using System.Text;
using Core;
using Core.AI;
using Core.AI.Models;
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
    : Agent(durableState, chatClient), ICodeOrchestrator
{
    static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMinutes(10);

    protected override string DisplayName => "Code Orchestrator";

    public static string AgentDescription => "Generates and executes standalone C# console apps that call agent grains directly to fulfill complex orchestration tasks.";
    public static string[] AgentCapabilities => ["orchestrate", "execute", "generate", "csharp", "code", "automate"];

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
        return $"""
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
        using Core.Contracts;
        using IAW.Agents.System;
        using IAW.Agents.Coding;

        var builder = Host.CreateApplicationBuilder(args);
        builder.AddIAWClient();
        using var host = builder.Build();
        await host.StartAsync();
        var client = host.Services.GetRequiredService<IClusterClient>();

        // YOUR CODE HERE

        await host.StopAsync();
        ```

        RULES:
        - Use `builder.AddIAWClient()` from namespace `Aspire.IAW` to connect to the cluster.
        - Get agents via client.GetGrain<IInterfaceName>("grain-id") — see catalog below for available agents.
        - Call await agent.GetResponse("prompt", default) to talk to agents. Use `default` for CancellationToken.
        - Always write result.json with status, summary, artifacts, and metrics fields
        - Keep code SHORT. Under 80 lines. No unnecessary abstractions.
        - Use simple string operations, not complex LINQ chains
        - Wrap everything in try/catch, write error to result.json in catch
        - Pick the MOST SPECIALIZED agent for the task — don't use IShell when a domain agent exists

        {agentCatalog}
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
