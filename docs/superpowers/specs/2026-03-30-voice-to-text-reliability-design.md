# Voice-to-Text Reliability & Observability

## Problem

Voice-to-text via Foundry Local silently fails on machines where:
- Native ONNX Runtime binaries aren't restored (missing ORT NuGet feed)
- Model download stalls (network, missing execution providers)
- `EnsureInitializedAsync` hangs forever holding a `SemaphoreSlim`, blocking all subsequent voice messages

No logs, traces, or metrics surface because initialization is lazy (first voice message) and the webhook handler's fire-and-forget `Task.Run` swallows the 5-minute timeout.

## Solution

Four changes that work together:

### 1. nuget.config with ORT Feed

Add `nuget.config` to solution root so `Microsoft.ML.OnnxRuntime.Foundry` native binaries restore on any machine:

```xml
<packageSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  <add key="ORT" value="https://aiinfra.pkgs.visualstudio.com/PublicPackages/_packaging/ORT/nuget/v3/index.json" />
</packageSources>
<packageSourceMapping>
  <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  <packageSource key="ORT"><package pattern="*Foundry*" /></packageSource>
</packageSourceMapping>
```

### 2. Eager Initialization via IHostedService

Replace lazy `EnsureInitializedAsync` with `IHostedService.StartAsync`:

- Model downloads and loads at app startup, not on first voice message
- Failures surface in Aspire Dashboard immediately via startup logs
- No semaphore-based lazy init that can deadlock
- Model is warm when the first voice message arrives

State tracking properties:
- `IsReady` — true after successful initialization
- `InitializationError` — stores the exception if init failed

`TranscribeAsync` throws immediately with stored error if init failed, instead of hanging.

Timeouts:
- 5 minutes for model download
- 2 minutes for model load
- Structured try/catch around each step with explicit error logging

### 3. WhisperHealthCheck

Health check registered conditionally when `AddWhisperProvider<T>` is called:

- Checks `IsReady` state on the transcription service
- Reports `Unhealthy` with error message if initialization failed
- Reports `Degraded` if still initializing
- Tagged with `"live"` to match existing health check pattern
- Surfaces in Aspire Dashboard health panel

### 4. AddWhisperProvider Upgrade

Current `AddWhisperProvider<T>` is a single-line singleton registration. Upgrade to:

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

Single instance shared across hosted service, interface, and health check registrations.

Remove duplicate `AddWhisperProvider` definition from `LlmRegistration.cs` (keep only the one in `IAWClientExtensions.cs`).

## Files Changed

| File | Change |
|------|--------|
| `nuget.config` (new) | ORT feed for native ONNX Runtime binaries |
| `src/Clients.Telegram/Services/FoundryLocalTranscriptionService.cs` | Implement `IHostedService`, add timeouts, structured error handling, state tracking |
| `src/Aspire.IAW.Client/IAWClientExtensions.cs` | Upgrade `AddWhisperProvider` with hosted service + health check |
| `src/Aspire.IAW.Client/WhisperHealthCheck.cs` (new) | `IHealthCheck` for Whisper readiness |
| `src/Aspire.IAW.Client/LlmRegistration.cs` | Remove duplicate `AddWhisperProvider` |

No changes to Core, AppHost, or Aspire.Hosting.IAW.

## Success Criteria

- On a fresh machine: `dotnet restore` pulls native ONNX binaries without manual feed config
- Aspire Dashboard shows Whisper health status at startup (not on first voice message)
- If Foundry Local fails to init, health check shows Unhealthy with actionable error
- Voice transcription works on second PC after these changes
- No silent hangs — all failure paths log and surface errors within timeout windows
