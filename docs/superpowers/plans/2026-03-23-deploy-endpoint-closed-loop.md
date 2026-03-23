# Deploy Endpoint & Closed Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the self-improvement loop by adding a `/deploy` endpoint to the MCP server (separate process) that stops the assistant, builds from source, and starts it fresh — plus a DurableJob safety net that verifies deployment health on restart.

**Architecture:** Hybrid A+C approach. The MCP server (already a separate process on port 5300) gets a `/deploy` HTTP endpoint that executes stop→build→start atomically. The Aspire agent fires a HTTP POST to it instead of restarting directly. A `deploy-verify` DurableJob persists to Azure Storage and fires on silo restart to confirm the deployment succeeded. If the build fails, the endpoint reverts via `git revert` and starts with the old code.

**Tech Stack:** ASP.NET Core minimal API, Orleans DurableJobs, `System.Diagnostics.Process`, xunit.v3

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/IAW.MCP/Deploy/DeployEndpoint.cs` | Create | `/deploy` HTTP endpoint: stop→build→start sequence |
| `src/IAW.MCP/Program.cs` | Modify | Register the deploy endpoint |
| `src/Agents/Infrastructure/AspireAgent.cs` | Modify | Call MCP `/deploy` instead of direct restart for deployments |
| `src/Agents/Infrastructure/IAspire.cs` | Modify | Add DeployAsync method |
| `test/Core.Tests/Scheduling/DeployVerifyJobTests.cs` | Create | Tests for the DurableJob verify logic |
| `src/Core/Deployment/DeployVerifier.cs` | Create | Shared deploy verification logic (testable, no Orleans dependency) |

---

### Task 1: Create DeployVerifier — testable deployment verification logic

**Files:**
- Create: `src/Core/Deployment/DeployVerifier.cs`
- Create: `test/Core.Tests/Scheduling/DeployVerifyJobTests.cs`

The verify logic must be testable without Orleans. Extract it as a plain C# class.

- [ ] **Step 1: Write the failing tests**

Create `test/Core.Tests/Scheduling/DeployVerifyJobTests.cs`:

```csharp
using Core.Deployment;

namespace IAW.Core.Tests.Scheduling;

public class DeployVerifyJobTests
{
    [Fact]
    public void VerifyBuildOutput_Success_ReturnsHealthy()
    {
        var result = DeployVerifier.VerifyBuildOutput("Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        Assert.True(result.Success);
        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public void VerifyBuildOutput_Failure_ReturnsUnhealthy()
    {
        var result = DeployVerifier.VerifyBuildOutput("Build FAILED.\n    3 Error(s)");
        Assert.False(result.Success);
        Assert.Equal(3, result.Errors);
    }

    [Fact]
    public void VerifyBuildOutput_NullOrEmpty_ReturnsUnhealthy()
    {
        var result = DeployVerifier.VerifyBuildOutput("");
        Assert.False(result.Success);
    }

    [Fact]
    public void ShouldRevert_BuildFailed_ReturnsTrue()
    {
        var result = new BuildVerification(false, 3, "Build FAILED");
        Assert.True(DeployVerifier.ShouldRevert(result));
    }

    [Fact]
    public void ShouldRevert_BuildSucceeded_ReturnsFalse()
    {
        var result = new BuildVerification(true, 0, "Build succeeded");
        Assert.False(DeployVerifier.ShouldRevert(result));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~DeployVerify" -v minimal`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement DeployVerifier**

Create `src/Core/Deployment/DeployVerifier.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Core.Deployment;

public static partial class DeployVerifier
{
    public static BuildVerification VerifyBuildOutput(string buildOutput)
    {
        if (string.IsNullOrWhiteSpace(buildOutput))
            return new BuildVerification(false, -1, "No build output");

        var errorMatch = ErrorCountRegex().Match(buildOutput);
        var errors = errorMatch.Success ? int.Parse(errorMatch.Groups[1].Value) : -1;
        var succeeded = buildOutput.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase) && errors == 0;

        return new BuildVerification(succeeded, errors, buildOutput);
    }

    public static bool ShouldRevert(BuildVerification verification)
        => !verification.Success;

    [GeneratedRegex(@"(\d+)\s+Error\(s\)")]
    private static partial Regex ErrorCountRegex();
}

public record BuildVerification(bool Success, int Errors, string Output);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~DeployVerify" -v minimal`
Expected: 5 PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Deployment/DeployVerifier.cs test/Core.Tests/Scheduling/DeployVerifyJobTests.cs
git commit -m "feat: add DeployVerifier with tests — build output parsing and revert logic"
```

---

### Task 2: Create /deploy endpoint in MCP server

**Files:**
- Create: `src/IAW.MCP/Deploy/DeployEndpoint.cs`
- Modify: `src/IAW.MCP/Program.cs`

- [ ] **Step 1: Create the deploy endpoint**

Create `src/IAW.MCP/Deploy/DeployEndpoint.cs`:

```csharp
using System.Diagnostics;
using Core.Deployment;

namespace IAW.MCP.Deploy;

public static class DeployEndpoint
{
    public static void MapDeployEndpoints(this WebApplication app)
    {
        app.MapPost("/deploy", async (ILogger<Program> logger, CancellationToken ct) =>
        {
            logger.LogInformation("Deploy: starting stop→build→start sequence");
            var iawRoot = FindIawRoot();
            if (iawRoot is null)
                return Results.Problem("Could not find IAW root directory");

            try
            {
                // Step 1: Stop assistant
                logger.LogInformation("Deploy: stopping assistant...");
                var stopResult = await RunProcessAsync("aspire", "mcp run execute_resource_command --resourceName assistant --commandName resource-stop", iawRoot, ct);
                if (stopResult.ExitCode != 0)
                {
                    // Try direct approach — stop via Aspire CLI
                    logger.LogWarning("Deploy: aspire mcp stop failed, trying resource command directly");
                }
                await Task.Delay(5000, ct); // Wait for DLLs to unlock

                // Step 2: Build
                logger.LogInformation("Deploy: building solution...");
                var buildResult = await RunProcessAsync("dotnet", "build IAW.slnx", iawRoot, ct);
                var verification = DeployVerifier.VerifyBuildOutput(buildResult.Output + buildResult.Error);

                if (!verification.Success)
                {
                    logger.LogError("Deploy: build FAILED with {Errors} errors. Reverting...", verification.Errors);
                    await RunProcessAsync("git", "checkout -- .", iawRoot, ct);
                    // Start with old code
                    await RunProcessAsync("aspire", "mcp run execute_resource_command --resourceName assistant --commandName resource-start", iawRoot, ct);
                    return Results.Json(new { success = false, action = "reverted", errors = verification.Errors, output = verification.Output[..Math.Min(2000, verification.Output.Length)] });
                }

                logger.LogInformation("Deploy: build succeeded. Starting assistant...");

                // Step 3: Start
                await RunProcessAsync("aspire", "mcp run execute_resource_command --resourceName assistant --commandName resource-start", iawRoot, ct);

                return Results.Json(new { success = true, action = "deployed", errors = 0 });
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

        var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = await process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);

        return (process.ExitCode, output, error);
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
```

- [ ] **Step 2: Register the endpoint in Program.cs**

In `src/IAW.MCP/Program.cs`, add `using IAW.MCP.Deploy;` and after `app.MapMcp();` add:

```csharp
app.MapDeployEndpoints();
```

- [ ] **Step 3: Build**

Run: `dotnet build src/IAW.MCP`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/IAW.MCP/Deploy/DeployEndpoint.cs src/IAW.MCP/Program.cs
git commit -m "feat: add /deploy endpoint to MCP server — stop→build→start sequence"
```

---

### Task 3: Wire Aspire agent to use /deploy endpoint

**Files:**
- Modify: `src/Agents/Infrastructure/IAspire.cs`
- Modify: `src/Agents/Infrastructure/AspireAgent.cs`

The Aspire agent's `RestartResourceAsync` currently calls MCP `execute_resource_command` directly. For deployments (code changes), it should call the MCP `/deploy` endpoint instead. Keep `RestartResourceAsync` for simple restarts (no code changes). Add a new `DeployAsync` for code deployments.

- [ ] **Step 1: Add DeployAsync to IAspire**

In `src/Agents/Infrastructure/IAspire.cs`, add:

```csharp
[Description("Deploy code changes to the assistant. Stops assistant, rebuilds from source, starts with fresh binary. Use after writing code changes. Slower than RestartResource but picks up new code.")]
Task<string> DeployAsync(CancellationToken ct = default);
```

- [ ] **Step 2: Implement DeployAsync in AspireAgent**

In `src/Agents/Infrastructure/AspireAgent.cs`, add the implementation. The MCP server runs on port 5300. Use `IHttpClientFactory` to call it. Add `IHttpClientFactory` to the constructor:

First, add `IHttpClientFactory httpClientFactory` to the constructor parameters. Then implement:

```csharp
public async Task<string> DeployAsync(CancellationToken ct = default)
{
    logger.LogInformation("Deploy: calling MCP /deploy endpoint");
    try
    {
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        var response = await httpClient.PostAsync("http://localhost:5300/deploy", null, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        logger.LogInformation("Deploy: result = {Body}", body);
        return body;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Deploy: failed");
        return $"Deploy failed: {ex.Message}";
    }
}
```

- [ ] **Step 3: Update Aspire instructions**

In `IAspire.cs`, update the instructions to mention Deploy:

Add to the RULES section:
```
- For deploying CODE CHANGES: call Deploy (stops, rebuilds, starts fresh binary).
- For simple restarts (no code changes): call RestartResource.
```

- [ ] **Step 4: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Agents/Infrastructure/IAspire.cs src/Agents/Infrastructure/AspireAgent.cs
git commit -m "feat: add DeployAsync to Aspire agent — calls MCP /deploy endpoint for code deployments"
```

---

### Task 4: Add deploy-verify DurableJob safety net

**Files:**
- Modify: `src/Agents/Infrastructure/AspireAgent.cs`

On activation, Aspire agent schedules a one-shot `deploy-verify` job that fires 60 seconds after startup to check deployment health.

- [ ] **Step 1: Add deploy-verify job scheduling in OnActivateAsync**

In `AspireAgent.cs`, in the `OnActivateAsync` method, after the log-monitor scheduling, add:

```csharp
if (!ScheduledJobs.ContainsKey("deploy-verify"))
{
    await ScheduleJob("deploy-verify", TimeSpan.FromSeconds(60),
        "Verify deployment health: check all resources are running.", cancellationToken);
}
```

Note: `ScheduleJob` (not `ScheduleRecurringJob`) — this is a one-shot job that fires once after startup.

- [ ] **Step 2: Handle deploy-verify in OnScheduledJobDueAsync**

In the existing `OnScheduledJobDueAsync` override, add a case for `deploy-verify` before the `log-monitor` case:

```csharp
if (job.Name == "deploy-verify")
{
    logger.LogInformation("Deploy verify: checking deployment health after restart");
    var resources = await ListResourcesAsync(ct);
    var healthy = resources.Contains("Running") && !resources.Contains("FailedToStart");

    if (!healthy)
    {
        logger.LogError("Deploy verify: UNHEALTHY after deployment!");
        await PublishAsync("deploy.verify.failed", new Dictionary<string, string>
        {
            ["summary"] = "Deployment verification failed — resources unhealthy after restart",
            ["details"] = resources
        }, ct);
    }
    else
    {
        logger.LogInformation("Deploy verify: all resources healthy after deployment");
        await PublishAsync("deploy.verify.succeeded", new Dictionary<string, string>
        {
            ["summary"] = "Deployment verified — all resources running"
        }, ct);
    }
    return;
}
```

- [ ] **Step 3: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Agents/Infrastructure/AspireAgent.cs
git commit -m "feat: add deploy-verify DurableJob — checks health 60s after restart"
```

---

### Task 5: Update SelfImprove to use Deploy instead of Restart

**Files:**
- Modify: `src/Agents/Orchestration/ThreadAgent.cs`

The SelfImprove tool's prompt tells Thread to use Aspire to restart. Update it to use Deploy instead.

- [ ] **Step 1: Update SelfImprove prompt**

In `ThreadAgent.cs`, find the `SelfImproveAsync` method. In the prompt string, change:

From: `"- Use Aspire to restart the assistant resource to deploy"`
To: `"- Use Aspire Deploy tool (not RestartResource) to deploy code changes — this stops, rebuilds, and starts fresh"`

- [ ] **Step 2: Build**

Run: `dotnet build src/Agents`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Orchestration/ThreadAgent.cs
git commit -m "feat: SelfImprove uses Deploy instead of Restart for code changes"
```

---

### Task 6: End-to-end test — IAW creates EmojiAgent via self-improvement

**Files:** None (testing via MCP)

This is the full closed-loop test. IAW must create the EmojiAgent, build it, deploy it, and respond to emoji requests — all autonomously.

- [ ] **Step 1: Build and start everything**

Build full solution, start Aspire AppHost, verify all resources running.

- [ ] **Step 2: Clean up any previous EmojiAgent files**

Remove `src/Agents/Fun/` if it exists from previous attempts.

- [ ] **Step 3: Send creation request via test harness**

Send to Thread:
```
Create a new EmojiAgent in the IAW system. Write IEmoji.cs and EmojiAgent.cs to E:\IAW\src\Agents\Fun\.
Follow the exact same patterns as existing agents. Use namespace IAW.Agents.Fun, extend Agent<IEmoji>,
use [Llm<Claude45Haiku>]. The agent responds to everything in pure emoji.
After writing files, use Aspire Deploy to deploy the changes (not RestartResource).
```

- [ ] **Step 4: Verify via Aspire traces**

Check traces for:
- FileSystem agent writing files
- DotNet agent building
- Aspire agent calling Deploy
- MCP `/deploy` endpoint executing stop→build→start
- `deploy-verify` DurableJob firing after restart

- [ ] **Step 5: Test the emoji agent**

Send to Thread: `Call SendToAgent with agentName Emoji and request: I love programming`
Expected: emoji response from the IAW-created EmojiAgent.

- [ ] **Step 6: Commit EmojiAgent files that IAW created**

```bash
git add src/Agents/Fun/
git commit -m "feat: EmojiAgent created by IAW self-improvement loop"
```
