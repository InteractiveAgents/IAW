using System.Diagnostics;
using System.Text;

namespace Core.Orchestration;

public class ScriptExecutor
{
    public async Task<ScriptResult> ExecuteScriptAsync(
        string programSource,
        string workingDirectory,
        Func<string, (bool Success, string[] Errors)>? validator = null,
        CancellationToken ct = default)
    {
        if (validator is not null)
        {
            var (success, errors) = validator(programSource);
            if (!success)
                return new ScriptResult(-1, string.Join("\n", errors)) { Error = "Compilation validation failed" };
        }

        var runDir = Path.Combine(workingDirectory, $"orchestration-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(runDir);

        var (ExitCode, Output) = await RunProcessAsync("dotnet", "new console --name Script --force", runDir, ct);
        if (ExitCode != 0)
            return new ScriptResult(ExitCode, $"Scaffold failed: {Output}");

        var projectDir = Path.Combine(runDir, "Script");
        var programPath = Path.Combine(projectDir, "Program.cs");
        await File.WriteAllTextAsync(programPath, programSource, ct);

        var runResult = await RunProcessAsync("dotnet", $"run --project \"{projectDir}\"", runDir, ct);

        return new ScriptResult(runResult.ExitCode, runResult.Output);
    }

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName, string arguments, string workingDirectory, CancellationToken ct)
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
        if (process is null)
            return (-1, $"Failed to start {fileName}");

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var output = new StringBuilder();
        if (stdout.Length > 0) output.AppendLine(stdout.Trim());
        if (stderr.Length > 0) output.AppendLine(stderr.Trim());

        return (process.ExitCode, output.ToString().Trim());
    }
}

[GenerateSerializer]
public record ScriptResult(
    [property: Id(0)] int ExitCode,
    [property: Id(1)] string Output)
{
    public bool Success => ExitCode == 0;
    [Id(2)] public string? Error { get; init; }
}
