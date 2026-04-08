using Core.AI;
using Core.Contracts;
using Core.Tools;
using IAW.Core;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace IAW.Agents.System;

public class ShellAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient)
    : Agent<IShell>(durableState, chatClient), IShell
{
    static readonly HashSet<string> BlockedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "format", "shutdown", "reboot", "mkfs", "dd", "fdisk", "diskpart"
    };

    static readonly string[] BlockedArgumentPatterns =
    [
        "rm -rf /",
        "del /s /q c:\\",
        "> /dev/null 2>&1 &",
        ":(){ :|:& };:"
    ];

    protected override IReadOnlyList<AITool> DefineTools()
    {
        var tools = new List<AITool>();
        RegisterToolMethods(tools, new ShellTools(() => GetWorkspacePath() ?? Directory.GetCurrentDirectory()));
        return tools;
    }

    static string? ValidateCommand(string command)
    {
        var normalized = command.Trim();
        var firstToken = normalized.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "";
        var commandName = Path.GetFileNameWithoutExtension(firstToken);

        if (BlockedCommands.Contains(commandName))
            return $"Command blocked: '{commandName}' is prohibited";

        foreach (var pattern in BlockedArgumentPatterns)
            if (normalized.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return $"Command blocked: contains prohibited pattern";

        return null;
    }

    public async Task<CommandResult> ExecuteAsync(
        string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var validationError = ValidateCommand(command);
        if (validationError is not null)
            return new CommandResult(-1, "", validationError, TimeSpan.Zero);

        var effectiveDirectory = workingDirectory ?? GetWorkspacePath() ?? Directory.GetCurrentDirectory();
        var sw = Stopwatch.StartNew();

        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c \"{command.Replace("\"", "\\\"")}\"" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            WorkingDirectory = effectiveDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            sw.Stop();
            return new CommandResult(-1, "", "Failed to start process", sw.Elapsed);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            sw.Stop();

            var result = new CommandResult(process.ExitCode, output, error, sw.Elapsed);
            await RecordCommandExecution(command, result, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            sw.Stop();

            var result = new CommandResult(-1, "", "Command timed out", sw.Elapsed);
            await RecordCommandExecution(command, result, ct);
            return result;
        }
    }

    public async Task<CommandResult> RunDotnetAsync(
        string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        var effectiveDirectory = workingDirectory ?? GetWorkspacePath() ?? Directory.GetCurrentDirectory();
        var sw = Stopwatch.StartNew();

        var psi = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = effectiveDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            sw.Stop();
            return new CommandResult(-1, "", "Failed to start dotnet process", sw.Elapsed);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(120_000);

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            sw.Stop();

            var result = new CommandResult(process.ExitCode, output, error, sw.Elapsed);
            await RecordCommandExecution($"dotnet {arguments}", result, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            sw.Stop();

            var result = new CommandResult(-1, "", "dotnet command timed out after 120s", sw.Elapsed);
            await RecordCommandExecution($"dotnet {arguments}", result, ct);
            return result;
        }
    }

    public async Task<CommandResult> ExecutePowerShellAsync(
        string command, string? workingDirectory = null, int timeoutMs = 120_000, CancellationToken ct = default)
    {
        var validationError = ValidateCommand(command);
        if (validationError is not null)
            return new CommandResult(-1, "", validationError, TimeSpan.Zero);

        var effectiveDirectory = workingDirectory ?? GetWorkspacePath() ?? Directory.GetCurrentDirectory();
        var sw = Stopwatch.StartNew();

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var (shell, args) = ResolvePowerShell(encoded);

        var psi = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            WorkingDirectory = effectiveDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            sw.Stop();
            return new CommandResult(-1, "", "Failed to start PowerShell process", sw.Elapsed);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var error = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            sw.Stop();

            var result = new CommandResult(process.ExitCode, TruncateOutput(output), TruncateOutput(error), sw.Elapsed);
            await RecordCommandExecution($"pwsh: {command}", result, ct);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            sw.Stop();

            var result = new CommandResult(-1, "", "PowerShell command timed out", sw.Elapsed);
            await RecordCommandExecution($"pwsh: {command}", result, ct);
            return result;
        }
    }

    static (string Shell, string Args) ResolvePowerShell(string encodedCommand)
    {
        if (OperatingSystem.IsWindows())
        {
            // pwsh (PS 7+) is preferred; fall back to powershell.exe (PS 5.1)
            var pwshPath = FindExecutable("pwsh");
            if (pwshPath is not null)
                return (pwshPath, $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}");
            return ("powershell.exe", $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}");
        }

        var linuxPwsh = FindExecutable("pwsh");
        return linuxPwsh is not null
            ? (linuxPwsh, $"-NoProfile -NonInteractive -EncodedCommand {encodedCommand}")
            : throw new InvalidOperationException("PowerShell (pwsh) is not installed on this system");
    }

    static string? FindExecutable(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", "" } : new[] { "" };

        foreach (var dir in pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, name + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }
        return null;
    }

    static string TruncateOutput(string output, int maxLength = 16_384)
    {
        if (output.Length <= maxLength) return output;

        var headSize = maxLength * 2 / 3;
        var tailSize = maxLength / 3;
        return $"{output[..headSize]}\n\n... [{output.Length - maxLength} characters truncated] ...\n\n{output[^tailSize..]}";
    }

    public Task<ShellMetrics> GetMetricsAsync(CancellationToken ct = default)
    {
        var totalCommands = GetCounterValue("total-commands");
        var failedCommands = GetCounterValue("failed-commands");
        var commandFrequency = DeserializeDictionary("command-frequency");
        var totalDurationMs = GetLongValue("total-duration-ms");
        var avgExecutionTime = totalCommands > 0
            ? TimeSpan.FromMilliseconds(totalDurationMs / totalCommands)
            : TimeSpan.Zero;

        return Task.FromResult(new ShellMetrics(totalCommands, failedCommands, commandFrequency, avgExecutionTime));
    }

    private async Task RecordCommandExecution(string command, CommandResult result, CancellationToken ct)
    {
        IncrementCounter("total-commands");
        if (result.ExitCode != 0)
            IncrementCounter("failed-commands");

        var totalDurationMs = GetLongValue("total-duration-ms") + (long)result.Duration.TotalMilliseconds;
        State["total-duration-ms"] = new StateEntry("total-duration-ms", totalDurationMs);

        var commandKey = ExtractCommandName(command);
        var frequency = DeserializeDictionary("command-frequency");
        frequency.TryGetValue(commandKey, out var currentCount);
        frequency[commandKey] = currentCount + 1;
        State["command-frequency"] = new StateEntry("command-frequency", JsonSerializer.Serialize(frequency));

        await WriteStateAsync(ct);

        var eventName = result.ExitCode == 0 ? "command.completed" : "command.failed";
        await PublishAsync(eventName, new Dictionary<string, string>
        {
            ["Command"] = command,
            ["ExitCode"] = result.ExitCode.ToString(),
            ["DurationMs"] = ((long)result.Duration.TotalMilliseconds).ToString()
        }, ct);
    }

    private static string ExtractCommandName(string command)
    {
        var trimmed = command.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;
    }

    private void IncrementCounter(string counterKey)
    {
        var current = GetCounterValue(counterKey);
        State[counterKey] = new StateEntry(counterKey, current + 1);
    }

    private int GetCounterValue(string counterKey)
    {
        if (!State.TryGetValue(counterKey, out var desc)) return 0;
        return desc.Value is int i ? i : int.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private long GetLongValue(string key)
    {
        if (!State.TryGetValue(key, out var desc)) return 0;
        return desc.Value is long l ? l : long.TryParse(desc.Value.ToString(), out var parsed) ? parsed : 0;
    }

    private Dictionary<string, int> DeserializeDictionary(string key)
    {
        if (!State.TryGetValue(key, out var desc))
            return new Dictionary<string, int>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(desc.Value.ToString()!)
                   ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, int>();
        }
    }
}