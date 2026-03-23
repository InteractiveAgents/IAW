using Core.Deployment;
using System.Diagnostics;

namespace IAW.MCP.Deploy;

public static class DeployEndpoint
{
    public static void MapDeployEndpoints(this WebApplication app)
    {
        app.MapPost("/deploy", async (ILogger<Program> logger, CancellationToken ct) =>
        {
            logger.LogInformation("Deploy: starting build sequence");
            var iawRoot = FindIawRoot();
            if (iawRoot is null)
                return Results.Problem("Could not find IAW root directory (looking for IAW.slnx)");

            try
            {
                logger.LogInformation("Deploy: building solution at {Root}", iawRoot);
                var (exitCode, output, error) = await RunProcessAsync(
                    "dotnet", "build src/IAW.Assistant/IAW.Assistant.csproj", iawRoot, ct);

                var fullOutput = output + "\n" + error;
                var verification = DeployVerifier.VerifyBuildOutput(fullOutput);

                if (!verification.Success)
                {
                    logger.LogError("Deploy: build FAILED with {Errors} errors", verification.Errors);

                    // Revert changes so next start uses working code
                    logger.LogWarning("Deploy: reverting changes via git checkout");
                    await RunProcessAsync("git", "checkout -- .", iawRoot, ct);

                    return Results.Json(new
                    {
                        success = false,
                        action = "reverted",
                        errors = verification.Errors,
                        output = fullOutput.Length > 2000 ? fullOutput[..2000] : fullOutput
                    });
                }

                logger.LogInformation("Deploy: build succeeded");
                return Results.Json(new
                {
                    success = true,
                    action = "built",
                    errors = 0
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Deploy: failed");
                return Results.Problem($"Deploy failed: {ex.Message}");
            }
        });
    }

    static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
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
        if (process is null) return (-1, "", "Failed to start process");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            return (-1, "", "Process timed out after 5 minutes");
        }
    }

    static string? FindIawRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "IAW.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
