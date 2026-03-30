# Voice-to-Text Reliability & Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make voice-to-text via Foundry Local reliable, observable, and fail-fast on any machine.

**Architecture:** Replace lazy semaphore-based initialization with eager `IHostedService` startup. Add health check, timeouts, structured error logging, and the ORT NuGet feed. No Core or AppHost changes needed.

**Tech Stack:** Microsoft.AI.Foundry.Local, ASP.NET Core health checks, IHostedService, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-30-voice-to-text-reliability-design.md`

---

### Task 1: Add nuget.config with ORT Feed

**Files:**
- Create: `nuget.config`

- [ ] **Step 1: Create nuget.config**

Create `nuget.config` at the solution root:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="ORT" value="https://aiinfra.pkgs.visualstudio.com/PublicPackages/_packaging/ORT/nuget/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="ORT">
      <package pattern="*Foundry*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

- [ ] **Step 2: Verify restore works**

Run: `dotnet restore IAW.slnx`
Expected: Restore succeeds. Packages with `Foundry` in the name resolve from the ORT feed.

- [ ] **Step 3: Commit**

```bash
git add nuget.config
git commit -m "build: add ORT NuGet feed for Foundry Local native binaries"
```

---

### Task 2: Add IWhisperReadiness Interface to Core

The health check (in `Aspire.IAW.Client`) needs to query the transcription service's readiness state. Since `IAudioTranscriptionService` lives in Core and doesn't expose readiness, add a small interface.

**Files:**
- Modify: `src/Core/AI/IAudioTranscriptionService.cs`

- [ ] **Step 1: Add IWhisperReadiness interface**

Add the `IWhisperReadiness` interface to the existing file. This is what the health check will query:

```csharp
namespace Core.AI;

public interface IAudioTranscriptionService : IAsyncDisposable
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
    Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default);
}

public interface IWhisperReadiness
{
    bool IsReady { get; }
    bool InitializationFailed { get; }
    string? ErrorMessage { get; }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Core/AI/IAudioTranscriptionService.cs
git commit -m "feat: add IWhisperReadiness interface for health check support"
```

---

### Task 3: Create WhisperHealthCheck

**Files:**
- Create: `src/Aspire.IAW.Client/WhisperHealthCheck.cs`

- [ ] **Step 1: Write the health check**

```csharp
using Core.AI;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.IAW;

internal sealed class WhisperHealthCheck(IAudioTranscriptionService transcriptionService) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (transcriptionService is not IWhisperReadiness readiness)
            return Task.FromResult(HealthCheckResult.Degraded("Transcription service does not report readiness"));

        if (readiness.IsReady)
            return Task.FromResult(HealthCheckResult.Healthy("Whisper model loaded"));

        if (readiness.InitializationFailed)
            return Task.FromResult(HealthCheckResult.Unhealthy($"Whisper initialization failed: {readiness.ErrorMessage}"));

        return Task.FromResult(HealthCheckResult.Degraded("Whisper model still initializing"));
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Aspire.IAW.Client/WhisperHealthCheck.cs
git commit -m "feat: add WhisperHealthCheck for Aspire Dashboard visibility"
```

---

### Task 4: Upgrade AddWhisperProvider Registration

**Files:**
- Modify: `src/Aspire.IAW.Client/IAWClientExtensions.cs:41-46`
- Modify: `src/Aspire.IAW.Client/LlmRegistration.cs:240-245`

- [ ] **Step 1: Upgrade AddWhisperProvider in IAWClientExtensions.cs**

Replace the existing `AddWhisperProvider` method (lines 41-46) with:

```csharp
    public static IHostApplicationBuilder AddWhisperProvider<TService>(this IHostApplicationBuilder builder)
        where TService : class, IAudioTranscriptionService, IHostedService
    {
        builder.Services.AddSingleton<TService>();
        builder.Services.AddSingleton<IAudioTranscriptionService>(sp => sp.GetRequiredService<TService>());
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TService>());
        builder.Services.AddHealthChecks()
            .AddCheck<WhisperHealthCheck>("whisper", tags: ["live"]);
        return builder;
    }
```

Add `using Microsoft.Extensions.Hosting;` to the imports if not already present.

- [ ] **Step 2: Remove duplicate from LlmRegistration.cs**

Delete the duplicate `AddWhisperProvider` method at lines 240-245 of `src/Aspire.IAW.Client/LlmRegistration.cs`:

```csharp
// DELETE THIS ENTIRE METHOD:
    public static IHostApplicationBuilder AddWhisperProvider<TService>(this IHostApplicationBuilder builder)
        where TService : class, IAudioTranscriptionService
    {
        builder.Services.AddSingleton<IAudioTranscriptionService, TService>();
        return builder;
    }
```

- [ ] **Step 3: Verify build**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds. The Telegram client calls `builder.AddWhisperProvider<FoundryLocalTranscriptionService>()` which now requires `IHostedService`. This will fail until Task 5 updates the service. That's expected — move to Task 5.

- [ ] **Step 4: Commit**

```bash
git add src/Aspire.IAW.Client/IAWClientExtensions.cs src/Aspire.IAW.Client/LlmRegistration.cs
git commit -m "feat: upgrade AddWhisperProvider with hosted service and health check"
```

---

### Task 5: Rewrite FoundryLocalTranscriptionService with IHostedService

This is the core change. Replace lazy semaphore init with eager `IHostedService` startup, add timeouts, structured error handling, and `IWhisperReadiness`.

**Files:**
- Modify: `src/Clients.Telegram/Services/FoundryLocalTranscriptionService.cs`

- [ ] **Step 1: Rewrite the service**

Replace the entire file content with:

```csharp
using Core.AI;
using Microsoft.AI.Foundry.Local;
using System.Text;

namespace TelegramClient.Services;

public sealed class FoundryLocalTranscriptionService : IAudioTranscriptionService, IHostedService, IWhisperReadiness
{
    private static readonly string[] OggExtensions = [".ogg", ".opus", ".oga"];
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(2);

    private readonly IConfiguration _configuration;
    private readonly IAudioConverter? _audioConverter;
    private readonly ILogger<FoundryLocalTranscriptionService> _logger;
    private Model? _model;

    public bool IsReady { get; private set; }
    public bool InitializationFailed { get; private set; }
    public string? ErrorMessage { get; private set; }

    public FoundryLocalTranscriptionService(
        IConfiguration configuration,
        ILogger<FoundryLocalTranscriptionService> logger,
        IAudioConverter? audioConverter = null)
    {
        _configuration = configuration;
        _logger = logger;
        _audioConverter = audioConverter;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Initializing Foundry Local for Whisper...");
            await InitializeFoundryManagerAsync(ct);

            var catalog = await FoundryLocalManager.Instance.GetCatalogAsync(ct);
            _model = await ResolveModelAsync(catalog);

            _logger.LogInformation("Downloading Whisper model {ModelId}...", _model.Id);
            using (var downloadCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                downloadCts.CancelAfter(DownloadTimeout);
                await _model.DownloadAsync();
            }

            _logger.LogInformation("Loading Whisper model {ModelId}...", _model.Id);
            using (var loadCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                loadCts.CancelAfter(LoadTimeout);
                await _model.LoadAsync();
            }

            IsReady = true;
            _logger.LogInformation("Whisper model ready: {ModelId}", _model.Id);
        }
        catch (Exception ex)
        {
            InitializationFailed = true;
            ErrorMessage = ex.Message;
            _logger.LogError(ex, "Whisper initialization failed");
            // don't rethrow — let the app start; health check will report unhealthy
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (IsReady && _model is not null)
        {
            _logger.LogInformation("Unloading Whisper model {ModelId}", _model.Id);
            try { await _model.UnloadAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error unloading Whisper model"); }
        }
    }

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);
        EnsureReady();

        var extension = Path.GetExtension(audioFilePath).ToLowerInvariant();
        var fileToTranscribe = audioFilePath;
        string? convertedFilePath = null;

        if (OggExtensions.Contains(extension) && _audioConverter is not null)
        {
            _logger.LogDebug("Converting {Source} to WAV", audioFilePath);
            convertedFilePath = _audioConverter.ConvertToWav(audioFilePath);
            fileToTranscribe = convertedFilePath;
        }

        try
        {
            _logger.LogDebug("Transcribing: {Path}", fileToTranscribe);
            var client = await _model!.GetAudioClientAsync();
            var result = new StringBuilder();

            await foreach (var chunk in client.TranscribeAudioStreamingAsync(fileToTranscribe, ct))
                result.Append(chunk.Text);

            var transcription = result.ToString().Trim();
            _logger.LogInformation("Transcription complete: {Length} chars", transcription.Length);
            return transcription;
        }
        finally
        {
            if (convertedFilePath is not null && File.Exists(convertedFilePath))
                File.Delete(convertedFilePath);
        }
    }

    public async Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audioStream);
        var tempPath = Path.Combine(Path.GetTempPath(), $"iaw_voice_{Guid.NewGuid()}{Path.GetExtension(fileName)}");
        try
        {
            await using (var fileStream = File.Create(tempPath))
                await audioStream.CopyToAsync(fileStream, ct);
            return await TranscribeAsync(tempPath, ct);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (IsReady && _model is not null)
        {
            try { await _model.UnloadAsync(); }
            catch { /* best-effort cleanup */ }
        }
    }

    private void EnsureReady()
    {
        if (IsReady) return;
        if (InitializationFailed)
            throw new InvalidOperationException($"Whisper is not available: {ErrorMessage}");
        throw new InvalidOperationException("Whisper model is still initializing");
    }

    private async Task InitializeFoundryManagerAsync(CancellationToken ct)
    {
        try
        {
            _ = FoundryLocalManager.Instance;
            _logger.LogDebug("Foundry Local manager already initialized");
        }
        catch (FoundryLocalException)
        {
            _logger.LogInformation("Creating new Foundry Local manager...");
            var config = new Configuration { AppName = "iaw" };
            await FoundryLocalManager.CreateAsync(config, _logger);
        }
    }

    private async Task<Model> ResolveModelAsync(ICatalog catalog)
    {
        var configuredId = _configuration[LlmConfig.WhisperModelId];

        if (!string.IsNullOrEmpty(configuredId))
        {
            var configured = await catalog.GetModelAsync(configuredId);
            if (configured is not null)
            {
                _logger.LogInformation("Using configured Whisper model: {ModelId}", configuredId);
                return configured;
            }
            _logger.LogWarning("Configured whisper model '{ModelId}' not found in catalog, falling back", configuredId);
        }

        foreach (var whisperModel in WhisperModel.All.OrderByDescending(m => m.Priority))
        {
            var found = await catalog.GetModelAsync(whisperModel.Id);
            if (found is not null)
            {
                _logger.LogInformation("Resolved Whisper model via fallback: {ModelId}", found.Id);
                return found;
            }
        }

        throw new InvalidOperationException(
            "No whisper model found in Foundry Local catalog. " +
            $"Expected one of: {string.Join(", ", WhisperModel.All.Select(m => m.Id))}");
    }
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds. The `IHostedService` constraint in `AddWhisperProvider` is now satisfied.

- [ ] **Step 3: Commit**

```bash
git add src/Clients.Telegram/Services/FoundryLocalTranscriptionService.cs
git commit -m "feat: rewrite FoundryLocalTranscriptionService with eager init, timeouts, health reporting"
```

---

### Task 6: Build and Verify

Full solution build + existing test pass.

**Files:** None (verification only)

- [ ] **Step 1: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Run existing Core tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~WhisperModelTests" -v normal`
Expected: All 5 WhisperModel tests pass (unchanged behavior).

- [ ] **Step 3: Run all tests**

Run: `dotnet test IAW.slnx -v normal`
Expected: All tests pass.

- [ ] **Step 4: Start Aspire and verify health**

Run: `dotnet run --project src/IAW.AppHost/Aspire.csproj`

Verify in Aspire Dashboard:
1. Telegram service starts
2. Structured logs show "Initializing Foundry Local for Whisper..."
3. Health check `/health` on the Telegram service shows `whisper` check status
4. If Foundry Local is available: status is Healthy with "Whisper model loaded"
5. If Foundry Local is not available: status is Unhealthy with the error message (not a silent hang)

- [ ] **Step 5: Test voice message (if Telegram is configured)**

Send a voice message via Telegram. Expected:
- Transcription succeeds and returns text immediately (model already loaded at startup)
- Or if init failed: bot logs the error, doesn't hang

---

### Task 7: Final Commit (if not already committed)

- [ ] **Step 1: Check for uncommitted changes**

Run: `git status`
If clean, skip. Otherwise commit any remaining changes.

```bash
git add -A
git commit -m "feat: voice-to-text reliability — eager init, health checks, ORT feed"
```
