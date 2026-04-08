using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Core.Tools;

public class ShellTools(Func<string> getWorkspacePath)
{
    private const int TimeoutMs = 120_000;
    private string WorkspacePath => getWorkspacePath();

    public ShellTools(string workspacePath) : this(() => workspacePath) { }

    [Description("Run a dotnet CLI command")]
    public Task<string> RunDotnetAsync(
        [Description("Arguments for 'dotnet' command")] string arguments,
        [Description("Working directory (defaults to workspace)")] string? workingDirectory = null)
        => ExecuteAsync("dotnet", arguments, workingDirectory ?? WorkspacePath);

    [Description("Run a shell command")]
    public Task<string> RunShellAsync(
        [Description("Command to execute")] string command,
        [Description("Working directory (defaults to workspace)")] string? workingDirectory = null)
    {
        var isWindows = OperatingSystem.IsWindows();
        var shell = isWindows ? "cmd.exe" : "/bin/sh";
        var escapedCommand = command.Replace("\"", "\\\"");
        var args = isWindows
            ? $"/c \"{escapedCommand}\""
            : $"-c \"{escapedCommand}\"";
        return ExecuteAsync(shell, args, workingDirectory ?? WorkspacePath);
    }

    [Description("Run a PowerShell command (pwsh or powershell.exe). Preferred for complex Windows tasks.")]
    public async Task<string> RunPowerShellAsync(
        [Description("PowerShell command or script to execute")] string command,
        [Description("Working directory (defaults to workspace)")] string? workingDirectory = null)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        var shell = OperatingSystem.IsWindows() ? "pwsh" : "pwsh";
        var args = $"-NoProfile -NonInteractive -EncodedCommand {encoded}";

        try
        {
            return await ExecuteAsync(shell, args, workingDirectory ?? WorkspacePath);
        }
        catch (Exception)
        {
            if (!OperatingSystem.IsWindows()) throw;
            return await ExecuteAsync("powershell.exe", args, workingDirectory ?? WorkspacePath);
        }
    }

    private static async Task<string> ExecuteAsync(string fileName, string arguments, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process is null) return $"Failed to start: {fileName}";
        using var cts = new CancellationTokenSource(TimeoutMs);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync(cts.Token);
        var sb = new StringBuilder();
        if (stdoutTask.Result.Length > 0) sb.AppendLine(stdoutTask.Result.Trim());
        if (stderrTask.Result.Length > 0) sb.AppendLine(stderrTask.Result.Trim());
        sb.AppendLine($"Exit code: {process.ExitCode}");
        var output = sb.ToString();
        if (output.Length <= 16_000) return output;
        var headSize = 10_000;
        var tailSize = 5_000;
        return $"{output[..headSize]}\n\n... [{output.Length - 15_000} characters truncated] ...\n\n{output[^tailSize..]}";

    }
}