using System.Diagnostics;
using System.Text;

namespace IAW.Core.Orchestration;

public class ScriptExecutor
{
    public async Task<ScriptResult> ExecuteScriptAsync(
        string programSource,
        string workingDirectory,
        CancellationToken ct = default)
    {
        var runDir = Path.Combine(workingDirectory, $"orchestration-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}");
        Directory.CreateDirectory(runDir);

        var scaffoldResult = await RunProcessAsync("dotnet", "new console --name Script --force", runDir, ct);
        if (scaffoldResult.ExitCode != 0)
            return new ScriptResult(scaffoldResult.ExitCode, $"Scaffold failed: {scaffoldResult.Output}");

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
}
