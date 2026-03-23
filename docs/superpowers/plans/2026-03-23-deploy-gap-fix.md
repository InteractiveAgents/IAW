# Deploy Gap Fix — Full Stop→Build→Start in /deploy Endpoint

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the deployment gap so IAW's self-improvement loop can write code, build, and deploy autonomously — the `/deploy` endpoint handles the full stop→build→start sequence from the MCP process.

**Architecture:** The `/deploy` endpoint (in MCP server, separate process) becomes the deployment orchestrator. It uses the Aspire dashboard's resource service gRPC API to stop/start the assistant. The Aspire agent's `DeployAsync` fires an HTTP POST to `/deploy` and accepts it'll die when the assistant stops. After the build succeeds, `/deploy` starts the assistant. If build fails, it reverts via git and starts with old code.

**Tech Stack:** ASP.NET Core, Aspire resource service API, `System.Diagnostics.Process`

---

## Root Cause Analysis

The current flow is broken:

```
AspireAgent.DeployAsync():
  1. RestartResourceAsync("assistant")  ← WRONG: does stop+START (assistant restarts before build)
  2. POST /deploy (build)               ← TOO LATE: assistant already restarted with old code
  3. Start assistant                    ← REDUNDANT: already started in step 1
```

The correct flow:

```
AspireAgent.DeployAsync():
  1. POST http://localhost:5300/deploy (fire-and-forget, accept death)

/deploy endpoint (in MCP process):
  1. Stop assistant via Aspire resource API
  2. Wait 5s for DLLs to unlock
  3. dotnet build src/IAW.Assistant/IAW.Assistant.csproj
  4. If build OK → Start assistant (fresh binary)
  5. If build FAIL → git checkout -- . → Start assistant (old code)
```

## DLL Lock Analysis

When building `src/IAW.Assistant/IAW.Assistant.csproj`:
- The build outputs `Agents.dll` to `src/Agents/bin/Debug/net11.0/`
- MCP locks `src/IAW.MCP/bin/Debug/net11.0/Agents.dll` (ITS copy, not the source)
- Telegram locks `src/Clients.Telegram/bin/Debug/net11.0/win-x64/Agents.dll` (ITS copy)
- Assistant is STOPPED → `src/IAW.Assistant/bin/` is unlocked

The build should succeed because it only copies TO the assistant's bin directory. The source output (`src/Agents/bin/`) is NOT locked by other processes — they have their own copies.

If the build still fails due to transitive locks, fallback: `dotnet build --no-dependencies src/IAW.Assistant/IAW.Assistant.csproj` or use `--artifacts-path E:\IAW\.deploy-artifacts`.

---

## File Map

| File | Action | Change |
|------|--------|--------|
| `src/IAW.MCP/Deploy/DeployEndpoint.cs` | Modify | Add Aspire resource stop/start via HTTP to dashboard API |
| `src/Agents/Infrastructure/AspireAgent.cs` | Modify | DeployAsync becomes fire-and-forget HTTP POST |
| `test/Core.Tests/Scheduling/DeployVerifyJobTests.cs` | Modify | Add test for revert-on-failure path |

---

### Task 1: Make /deploy endpoint handle full stop→build→start

**Files:**
- Modify: `src/IAW.MCP/Deploy/DeployEndpoint.cs`

The endpoint currently only builds. Change it to:
1. Stop assistant via Aspire resource API
2. Wait for DLLs to unlock
3. Build assistant project
4. Start assistant (or revert + start on failure)

The Aspire dashboard exposes a resource service at its OTLP endpoint. But the simplest approach: use the `aspire` CLI tool to execute resource commands, since it's already available.

- [ ] **Step 1: Update deploy endpoint**

Replace the handler to include stop/start:

```csharp
app.MapPost("/deploy", async (ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation("Deploy: starting full stop→build→start sequence");
    var iawRoot = FindIawRoot();
    if (iawRoot is null)
        return Results.Problem("Could not find IAW root directory");

    try
    {
        // Step 1: Stop assistant to release DLL locks
        logger.LogInformation("Deploy: stopping assistant resource");
        var appHostPath = Path.Combine(iawRoot, "src", "IAW.AppHost");
        await RunProcessAsync("aspire", "mcp run execute_resource_command -- --resourceName assistant --commandName resource-stop",
            appHostPath, ct);
        await Task.Delay(5000, ct); // Wait for process to die and DLLs to unlock

        // Step 2: Build
        logger.LogInformation("Deploy: building assistant project");
        var (exitCode, output, error) = await RunProcessAsync(
            "dotnet", "build src/IAW.Assistant/IAW.Assistant.csproj", iawRoot, ct);

        var fullOutput = output + "\n" + error;
        var verification = DeployVerifier.VerifyBuildOutput(fullOutput);

        if (!verification.Success)
        {
            logger.LogError("Deploy: build FAILED. Reverting and starting old code.");
            await RunProcessAsync("git", "checkout -- .", iawRoot, ct);
        }

        // Step 3: Start assistant (fresh binary if build succeeded, old code if reverted)
        logger.LogInformation("Deploy: starting assistant resource");
        await RunProcessAsync("aspire", "mcp run execute_resource_command -- --resourceName assistant --commandName resource-start",
            appHostPath, ct);

        if (!verification.Success)
        {
            return Results.Json(new { success = false, action = "reverted", errors = verification.Errors,
                output = fullOutput.Length > 2000 ? fullOutput[..2000] : fullOutput });
        }

        return Results.Json(new { success = true, action = "deployed", errors = 0 });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Deploy: sequence failed");
        // Try to start assistant even on error (recovery)
        try
        {
            var appHostPath = Path.Combine(iawRoot, "src", "IAW.AppHost");
            await RunProcessAsync("aspire", "mcp run execute_resource_command -- --resourceName assistant --commandName resource-start",
                appHostPath, CancellationToken.None);
        }
        catch { /* best effort */ }
        return Results.Problem($"Deploy failed: {ex.Message}");
    }
});
```

NOTE: The `aspire mcp run` command syntax may differ. If it doesn't work, fall back to calling the Aspire dashboard gRPC API directly, or use `curl` to the Aspire resource service endpoint (available via `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` env var).

Alternative if `aspire mcp run` doesn't work for resource commands: the MCP server already has access to the Aspire dashboard URL. Use the Aspire resource service gRPC endpoint:

```csharp
// Alternative: use Aspire resource service directly
var aspireEndpoint = Environment.GetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL");
// Call gRPC to stop/start
```

Or simplest fallback: just build and let Aspire handle the start. The caller (Aspire agent) already stopped the assistant before dying.

- [ ] **Step 2: Build and verify**

Run: `dotnet build src/IAW.MCP`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/IAW.MCP/Deploy/DeployEndpoint.cs
git commit -m "fix: /deploy endpoint handles full stop→build→start sequence"
```

---

### Task 2: Fix AspireAgent.DeployAsync — fire and forget

**Files:**
- Modify: `src/Agents/Infrastructure/AspireAgent.cs`

Current DeployAsync calls RestartResourceAsync (stop+start) THEN /deploy. This is wrong — the assistant restarts before the build. Fix: just POST to /deploy (which now handles stop+build+start) and accept that the agent will die.

- [ ] **Step 1: Simplify DeployAsync**

Replace the current implementation:

```csharp
public async Task<string> DeployAsync(CancellationToken ct = default)
{
    logger.LogInformation("Deploy: firing deploy request to MCP endpoint");

    try
    {
        // Fire the deploy request — the MCP endpoint handles stop→build→start
        // This agent will die when the assistant stops, so we don't await the full response
        using var httpClient = httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10); // Short timeout — we'll die before it completes

        _ = httpClient.PostAsync("http://localhost:5300/deploy", null, CancellationToken.None);

        // Give the HTTP request time to reach MCP before we die
        await Task.Delay(2000, ct);

        return "Deploy initiated. Assistant will restart with fresh binary.";
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Deploy: failed to initiate");
        return $"Deploy initiation failed: {ex.Message}";
    }
}
```

Key change: we fire the POST and DON'T await the full response. The MCP endpoint runs independently. We give it 2 seconds to receive the request, then return. The assistant will be stopped by the MCP endpoint shortly after.

- [ ] **Step 2: Build and test**

Run: `dotnet build src/Agents && dotnet test test/Core.Tests -v minimal`
Expected: 0 errors, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Agents/Infrastructure/AspireAgent.cs
git commit -m "fix: DeployAsync fires POST to /deploy and accepts death — no more stop+start before build"
```

---

### Task 3: End-to-end test — full closed loop

**Files:** None (testing via MCP)

- [ ] **Step 1: Kill all processes, clean EmojiAgent, rebuild, start Aspire**

- [ ] **Step 2: Ask IAW to create EmojiAgent**

Send: "Create EmojiAgent at E:\IAW\src\Agents\Fun/ then deploy via Aspire Deploy"

- [ ] **Step 3: Verify via traces**

Check:
- FileSystem wrote files
- DotNet built successfully
- Aspire Deploy was called
- /deploy endpoint stopped assistant, built, started
- Assistant came back with EmojiAgent registered

- [ ] **Step 4: Test emoji agent**

Send: "Call SendToAgent Emoji: I love coffee"
Expected: emoji response from IAW-created agent
