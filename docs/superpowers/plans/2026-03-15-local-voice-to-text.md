# Local Voice-to-Text (Foundry Local + Whisper) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add local voice-to-text transcription using Aspire-managed Foundry Local + Whisper models, integrated into IAW's AI configuration system with a clean `.WithVoice2Text<WhisperLargeV3Turbo>()` AppHost API — no external API calls, no magic strings.

**Architecture:** Aspire manages the Foundry Local lifecycle via `AddAzureAIFoundry().RunAsFoundryLocal()`. The whisper model is declared as a deployment, and Aspire handles download/loading/health checks. The transcription service (`FoundryLocalTranscriptionService`) calls the local OpenAI-compatible HTTP endpoint (`/v1/audio/transcriptions`) — no in-process SDK needed in Core, just standard `OpenAI.AudioClient`. The `WhisperModel` type hierarchy and `IAudioTranscriptionService` interface live in `IAW.Core`. The AppHost declares `.WithVoice2Text<WhisperLargeV3Turbo>()` which adds the Foundry Local resource and propagates the endpoint via Aspire connection strings.

**Tech Stack:** Aspire.Hosting.Azure.AIFoundry (AppHost), OpenAI SDK (audio transcription client), Whisper models (whisper-large-v3-turbo, whisper-small, whisper-tiny), Concentus + NAudio (OGG/WAV conversion)

**Reference implementation:** `E:\sources\Projects\Brain\src\Assistant.Telegram\Services\AudioTranscriptionService.cs`

---

## File Structure

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Core/AI/WhisperModel.cs` | Create | Base class + `EnsureAllModelsLoaded()` (mirrors `LLMModel` pattern) |
| `src/Core/AI/Models/WhisperLargeV3Turbo.cs` | Create | Typed model class (one per file, matches codebase convention) |
| `src/Core/AI/Models/WhisperSmall.cs` | Create | Typed model class |
| `src/Core/AI/Models/WhisperTiny.cs` | Create | Typed model class |
| `src/Core/AI/LlmConfig.cs` | Modify | Add `WhisperEndpoint` config constant |
| `src/Core/AI/IAudioTranscriptionService.cs` | Create | Interface with `IAsyncDisposable` |
| `src/Core/AI/IAudioConverter.cs` | Create | Sync `ConvertToWav(string)` interface in Core |
| `src/Core/AI/FoundryLocalTranscriptionService.cs` | Create | Calls local OpenAI-compatible `/v1/audio/transcriptions` endpoint, handles OGG conversion |
| `src/Core/AI/LlmRegistration.cs` | Modify | Add `AddWhisperProvider()` extension method |
| `src/IAW.AppHost/IAWExtensions.cs` | Modify | Add `.WithVoice2Text()` / `.WithVoice2Text<T>()` — creates Foundry Local resource + whisper deployment |
| `src/IAW.AppHost/Aspire.csproj` | Modify | Add `Aspire.Hosting.Azure.AIFoundry` package |
| `src/Clients.Telegram/Services/VoiceTranscriptionService.cs` | Delete | Replaced by Core's `IAudioTranscriptionService` |
| `src/Clients.Telegram/Services/AudioConverter.cs` | Modify | Implement Core's `IAudioConverter` interface |
| `src/Clients.Telegram/TelegramBotService.cs` | Modify | Use `IAudioTranscriptionService` |
| `src/Clients.Telegram/Program.cs` | Modify | Call `builder.AddWhisperProvider()`, register `IAudioConverter` |
| `src/Clients.Telegram/Telegram.csproj` | Modify | Remove `OpenAI` direct dep (comes via Core transitively) |
| `Directory.Packages.props` | Modify | Add `Aspire.Hosting.Azure.AIFoundry` version |
| `test/Core.Tests/WhisperModelTests.cs` | Create | Unit tests for WhisperModel registry |
| `src/IAW.AppHost/AppHost.cs` | Modify | Add `.WithVoice2Text()` call |

---

## Chunk 1: Core Whisper Model & Service Interface

### Task 1: Define WhisperModel base class

**Files:**
- Create: `src/Core/AI/WhisperModel.cs`

- [ ] **Step 1: Create WhisperModel base class**

```csharp
// src/Core/AI/WhisperModel.cs
namespace Core.AI;

public abstract class WhisperModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract int Priority { get; }
    public virtual string Version => "1";
    public virtual string Publisher => "OpenAI";

    private static readonly List<WhisperModel> _registry = [];
    private static readonly Lock _lock = new();

    public static IReadOnlyList<WhisperModel> All
    {
        get { lock (_lock) { return [.. _registry]; } }
    }

    protected WhisperModel()
    {
        lock (_lock) { _registry.Add(this); }
    }

    public static WhisperModel? FindById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void EnsureAllModelsLoaded()
    {
        _ = Models.WhisperLargeV3Turbo.Instance;
        _ = Models.WhisperSmall.Instance;
        _ = Models.WhisperTiny.Instance;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Core/AI/WhisperModel.cs
git commit -m "feat: add WhisperModel base class with priority and EnsureAllModelsLoaded"
```

### Task 2: Create concrete WhisperModel classes (one per file)

**Files:**
- Create: `src/Core/AI/Models/WhisperLargeV3Turbo.cs`
- Create: `src/Core/AI/Models/WhisperSmall.cs`
- Create: `src/Core/AI/Models/WhisperTiny.cs`

- [ ] **Step 1: Create WhisperLargeV3Turbo**

```csharp
// src/Core/AI/Models/WhisperLargeV3Turbo.cs
namespace Core.AI.Models;

public sealed class WhisperLargeV3Turbo : WhisperModel
{
    public static readonly WhisperLargeV3Turbo Instance = new();
    private WhisperLargeV3Turbo() { }
    public override string Id => "whisper-large-v3-turbo";
    public override string DisplayName => "Whisper Large V3 Turbo";
    public override int Priority => 100;
}
```

- [ ] **Step 2: Create WhisperSmall**

```csharp
// src/Core/AI/Models/WhisperSmall.cs
namespace Core.AI.Models;

public sealed class WhisperSmall : WhisperModel
{
    public static readonly WhisperSmall Instance = new();
    private WhisperSmall() { }
    public override string Id => "whisper-small";
    public override string DisplayName => "Whisper Small";
    public override int Priority => 50;
}
```

- [ ] **Step 3: Create WhisperTiny**

```csharp
// src/Core/AI/Models/WhisperTiny.cs
namespace Core.AI.Models;

public sealed class WhisperTiny : WhisperModel
{
    public static readonly WhisperTiny Instance = new();
    private WhisperTiny() { }
    public override string Id => "whisper-tiny";
    public override string DisplayName => "Whisper Tiny";
    public override int Priority => 10;
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Core/AI/Models/Whisper*.cs
git commit -m "feat: add WhisperLargeV3Turbo, WhisperSmall, WhisperTiny model definitions"
```

### Task 3: Add config constants and audio interfaces

**Files:**
- Modify: `src/Core/AI/LlmConfig.cs`
- Create: `src/Core/AI/IAudioTranscriptionService.cs`
- Create: `src/Core/AI/IAudioConverter.cs`

- [ ] **Step 1: Add whisper config keys to LlmConfig**

Add after `GitHubToken` line:

```csharp
public const string WhisperEndpoint = "AI:Whisper:Endpoint";
public const string WhisperModelId = "AI:Whisper:ModelId";
```

- [ ] **Step 2: Create IAudioTranscriptionService**

```csharp
// src/Core/AI/IAudioTranscriptionService.cs
namespace Core.AI;

public interface IAudioTranscriptionService : IAsyncDisposable
{
    Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default);
    Task<string> TranscribeAsync(Stream audioStream, string fileName, CancellationToken ct = default);
}
```

- [ ] **Step 3: Create IAudioConverter in Core**

```csharp
// src/Core/AI/IAudioConverter.cs
namespace Core.AI;

public interface IAudioConverter
{
    string ConvertToWav(string inputPath);
}
```

- [ ] **Step 4: Commit**

```bash
git add src/Core/AI/LlmConfig.cs src/Core/AI/IAudioTranscriptionService.cs src/Core/AI/IAudioConverter.cs
git commit -m "feat: add IAudioTranscriptionService, IAudioConverter interfaces and whisper config"
```

### Task 4: Write WhisperModel unit tests

**Files:**
- Create: `test/Core.Tests/WhisperModelTests.cs`

- [ ] **Step 1: Write tests**

```csharp
// test/Core.Tests/WhisperModelTests.cs
using Core.AI;
using Core.AI.Models;
using Xunit;

namespace IAW.Core.Tests;

public class WhisperModelTests
{
    [Fact]
    public void EnsureAllModelsLoaded_PopulatesRegistry()
    {
        WhisperModel.EnsureAllModelsLoaded();
        Assert.Contains(WhisperModel.All, m => m is WhisperLargeV3Turbo);
        Assert.Contains(WhisperModel.All, m => m is WhisperSmall);
        Assert.Contains(WhisperModel.All, m => m is WhisperTiny);
    }

    [Theory]
    [InlineData("whisper-large-v3-turbo")]
    [InlineData("whisper-small")]
    [InlineData("whisper-tiny")]
    public void FindById_ReturnsCorrectModel(string id)
    {
        WhisperModel.EnsureAllModelsLoaded();
        var model = WhisperModel.FindById(id);
        Assert.NotNull(model);
        Assert.Equal(id, model.Id);
    }

    [Fact]
    public void FindById_UnknownId_ReturnsNull()
    {
        var model = WhisperModel.FindById("whisper-nonexistent");
        Assert.Null(model);
    }

    [Fact]
    public void Priority_LargeHigherThanSmall()
    {
        WhisperModel.EnsureAllModelsLoaded();
        var large = WhisperModel.FindById("whisper-large-v3-turbo")!;
        var small = WhisperModel.FindById("whisper-small")!;
        var tiny = WhisperModel.FindById("whisper-tiny")!;
        Assert.True(large.Priority > small.Priority);
        Assert.True(small.Priority > tiny.Priority);
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~WhisperModelTests" -v minimal`

- [ ] **Step 3: Commit**

```bash
git add test/Core.Tests/WhisperModelTests.cs
git commit -m "test: add WhisperModel unit tests"
```

---

## Chunk 2: Foundry Local Transcription Service (HTTP-based)

### Task 5: Implement FoundryLocalTranscriptionService

**Files:**
- Create: `src/Core/AI/FoundryLocalTranscriptionService.cs`

This version calls the Foundry Local OpenAI-compatible HTTP endpoint (`/v1/audio/transcriptions`) that Aspire starts and manages. No in-process `Microsoft.AI.Foundry.Local` SDK needed in Core — just `OpenAI.AudioClient` (already a dependency via `OpenAI` package in Core.csproj).

- [ ] **Step 1: Implement the service**

```csharp
// src/Core/AI/FoundryLocalTranscriptionService.cs
using System.ClientModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Audio;

namespace Core.AI;

public sealed class FoundryLocalTranscriptionService : IAudioTranscriptionService
{
    private static readonly string[] OggExtensions = [".ogg", ".opus", ".oga"];

    private readonly IConfiguration _configuration;
    private readonly IAudioConverter? _audioConverter;
    private readonly ILogger<FoundryLocalTranscriptionService> _logger;

    public FoundryLocalTranscriptionService(
        IConfiguration configuration,
        ILogger<FoundryLocalTranscriptionService> logger,
        IAudioConverter? audioConverter = null)
    {
        _configuration = configuration;
        _logger = logger;
        _audioConverter = audioConverter;
    }

    public async Task<string> TranscribeAsync(string audioFilePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioFilePath);

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
            var endpoint = _configuration[LlmConfig.WhisperEndpoint]
                ?? throw new InvalidOperationException(
                    $"Whisper endpoint not configured. Set {LlmConfig.WhisperEndpoint} or add .WithVoice2Text() in AppHost.");

            var modelId = _configuration[LlmConfig.WhisperModelId] ?? "whisper-large-v3-turbo";

            var client = new AudioClient(modelId,
                new ApiKeyCredential("not-required"),
                new OpenAI.OpenAIClientOptions { Endpoint = new Uri(endpoint) });

            _logger.LogDebug("Transcribing {Path} via {Endpoint}", fileToTranscribe, endpoint);

            await using var audioStream = File.OpenRead(fileToTranscribe);
            var transcription = await client.TranscribeAudioAsync(
                audioStream, Path.GetFileName(fileToTranscribe), cancellationToken: ct);

            var text = transcription.Value.Text?.Trim() ?? "";
            _logger.LogInformation("Transcription complete: {Length} chars", text.Length);
            return text;
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Core/Core.csproj`

- [ ] **Step 3: Commit**

```bash
git add src/Core/AI/FoundryLocalTranscriptionService.cs
git commit -m "feat: implement FoundryLocalTranscriptionService using OpenAI-compatible HTTP endpoint"
```

### Task 6: Add AddWhisperProvider extension method

**Files:**
- Modify: `src/Core/AI/LlmRegistration.cs`

- [ ] **Step 1: Add after `AddEmbeddingProvider`**

```csharp
public static IHostApplicationBuilder AddWhisperProvider(this IHostApplicationBuilder builder)
{
    WhisperModel.EnsureAllModelsLoaded();
    builder.Services.AddSingleton<IAudioTranscriptionService, FoundryLocalTranscriptionService>();
    return builder;
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Core/AI/LlmRegistration.cs
git commit -m "feat: add AddWhisperProvider() extension for DI registration"
```

---

## Chunk 3: AppHost Integration (.WithVoice2Text API)

### Task 7: Add Aspire.Hosting.Azure.AIFoundry package

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/IAW.AppHost/Aspire.csproj`

- [ ] **Step 1: Add package version to Directory.Packages.props**

Add after `Aspire.Hosting.Azure.Storage`:

```xml
<PackageVersion Include="Aspire.Hosting.Azure.AIFoundry" Version="13.1.2" />
```

- [ ] **Step 2: Add package reference to Aspire.csproj**

```xml
<PackageReference Include="Aspire.Hosting.Azure.AIFoundry" />
```

- [ ] **Step 3: Commit**

```bash
git add Directory.Packages.props src/IAW.AppHost/Aspire.csproj
git commit -m "chore: add Aspire.Hosting.Azure.AIFoundry package"
```

### Task 8: Add `.WithVoice2Text()` to IAWExtensions

**Files:**
- Modify: `src/IAW.AppHost/IAWExtensions.cs`

- [ ] **Step 1: Add state fields**

After `_ollamaModelResources`:

```csharp
private static WhisperModel? _whisperModel;
private static IResourceBuilder<AzureAIFoundryDeploymentResource>? _whisperDeployment;
```

- [ ] **Step 2: Clear state in AddIAW**

After `_ollamaModelResources.Clear();`:

```csharp
_whisperModel = null;
_whisperDeployment = null;
```

- [ ] **Step 3: Add WithVoice2Text methods**

After `WithOllama`:

```csharp
public static OrleansService WithVoice2Text(this OrleansService orleans)
{
    WhisperModel.EnsureAllModelsLoaded();
    _whisperModel = WhisperModel.All.OrderByDescending(m => m.Priority).First();
    CreateFoundryWhisperDeployment();
    return orleans;
}

public static OrleansService WithVoice2Text<TModel>(this OrleansService orleans)
    where TModel : WhisperModel
{
    WhisperModel.EnsureAllModelsLoaded();
    _whisperModel = WhisperModel.All.OfType<TModel>().First();
    CreateFoundryWhisperDeployment();
    return orleans;
}

private static void CreateFoundryWhisperDeployment()
{
    if (_appBuilder is null || _whisperModel is null)
        throw new InvalidOperationException("Call AddIAW() before WithVoice2Text().");

    var foundry = _appBuilder.AddAzureAIFoundry("foundry")
        .RunAsFoundryLocal();
    _whisperDeployment = foundry.AddDeployment(
        "whisper", _whisperModel.Id, _whisperModel.Version, _whisperModel.Publisher);
}
```

- [ ] **Step 4: Propagate whisper config in WithLLMEnvironment**

Before `return builder;`:

```csharp
if (_whisperModel is not null)
    builder.WithEnvironment("AI__Whisper__ModelId", _whisperModel.Id);

if (_whisperDeployment is not null)
{
    builder.WithReference(_whisperDeployment);
    builder.WaitFor(_whisperDeployment);
}
```

> Note: The Aspire Foundry Local integration exposes the endpoint via connection string. `FoundryLocalTranscriptionService` reads it from `AI:Whisper:Endpoint`. We may need to map the Aspire connection string to this config key. Check what `WithReference(_whisperDeployment)` produces — it may set `ConnectionStrings:whisper` or similar. If so, update `FoundryLocalTranscriptionService` to also check connection strings, or add a `WithEnvironment` mapping.

- [ ] **Step 5: Verify build**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`

- [ ] **Step 6: Commit**

```bash
git add src/IAW.AppHost/IAWExtensions.cs
git commit -m "feat: add .WithVoice2Text() and .WithVoice2Text<T>() using Aspire Foundry Local"
```

### Task 9: Wire up in AppHost.cs

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs`

- [ ] **Step 1: Add .WithVoice2Text() to builder chain**

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text<WhisperLargeV3Turbo>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());
```

Add using: `using Core.AI.Models;` (if not already present — it is for LLM models)

- [ ] **Step 2: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat: enable .WithVoice2Text<WhisperLargeV3Turbo>() in AppHost"
```

---

## Chunk 4: Telegram Client Integration

### Task 10: Refactor AudioConverter to implement Core interface

**Files:**
- Modify: `src/Clients.Telegram/Services/AudioConverter.cs`

- [ ] **Step 1: Implement Core.AI.IAudioConverter**

```csharp
// src/Clients.Telegram/Services/AudioConverter.cs
using Concentus;
using Concentus.Oggfile;
using Core.AI;
using NAudio.Wave;

namespace TelegramClient.Services;

public sealed class AudioConverter : IAudioConverter
{
    const int SampleRate = 48000;
    const int BitsPerSample = 16;
    const int Channels = 1;
    const int BytesPerSample = 2;

    public string ConvertToWav(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var outputPath = Path.Combine(Path.GetTempPath(), $"iaw_converted_{Guid.NewGuid()}.wav");

        using var fileIn = File.OpenRead(inputPath);
        using var decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
        var oggIn = new OpusOggReadStream(decoder, fileIn);
        using var wavWriter = new WaveFileWriter(outputPath, new WaveFormat(SampleRate, BitsPerSample, Channels));

        while (oggIn.HasNextPacket)
        {
            var samples = oggIn.DecodeNextPacket();
            if (samples is not null && samples.Length > 0)
            {
                var bytes = new byte[samples.Length * BytesPerSample];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                wavWriter.Write(bytes, 0, bytes.Length);
            }
        }

        return outputPath;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/Clients.Telegram/Services/AudioConverter.cs
git commit -m "refactor: AudioConverter implements Core.AI.IAudioConverter"
```

### Task 11: Switch Telegram to IAudioTranscriptionService

**Files:**
- Delete: `src/Clients.Telegram/Services/VoiceTranscriptionService.cs`
- Modify: `src/Clients.Telegram/TelegramBotService.cs`
- Modify: `src/Clients.Telegram/Program.cs`
- Modify: `src/Clients.Telegram/Telegram.csproj`

- [ ] **Step 1: Delete VoiceTranscriptionService.cs**

```bash
git rm src/Clients.Telegram/Services/VoiceTranscriptionService.cs
```

- [ ] **Step 2: Update TelegramBotService constructor**

Replace `IVoiceTranscriptionService voiceService,` and `IAudioConverter audioConverter,` with:
`IAudioTranscriptionService transcriptionService,`

Add using: `using Core.AI;`

- [ ] **Step 3: Update TranscribeVoiceAsync**

OGG conversion now happens inside `FoundryLocalTranscriptionService`, so just download and pass directly:

```csharp
private async Task<string> TranscribeVoiceAsync(string fileId, CancellationToken ct)
{
    var file = await botClient.GetFileAsync(fileId);
    var downloadUrl = $"{botClient.Options.ServerAddress}/file/bot{options.Value.BotToken}/{file.FilePath}";

    using var http = httpClientFactory.CreateClient();
    await using var responseStream = await http.GetStreamAsync(downloadUrl, ct);

    var tempPath = Path.Combine(Path.GetTempPath(), $"iaw_voice_{Guid.NewGuid()}.ogg");
    try
    {
        await using (var fileStream = File.Create(tempPath))
            await responseStream.CopyToAsync(fileStream, ct);
        return await transcriptionService.TranscribeAsync(tempPath, ct);
    }
    finally
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
    }
}
```

- [ ] **Step 4: Update Program.cs**

```csharp
using Core.AI;

// Replace old registrations with:
builder.Services.AddSingleton<IAudioConverter, AudioConverter>();
builder.AddWhisperProvider();
```

- [ ] **Step 5: Remove OpenAI from Telegram.csproj**

Remove `<PackageReference Include="OpenAI" />` (available transitively via Core).

- [ ] **Step 6: Verify build**

Run: `dotnet build src/Clients.Telegram/Telegram.csproj`

- [ ] **Step 7: Commit**

```bash
git add -A src/Clients.Telegram/
git commit -m "feat: switch Telegram voice to local Whisper via IAudioTranscriptionService"
```

---

## Chunk 5: Final Wiring & Testing

### Task 12: Full build and test suite

- [ ] **Step 1:** `dotnet build IAW.slnx`
- [ ] **Step 2:** `dotnet test IAW.slnx --verbosity minimal`
- [ ] **Step 3:** Commit any fixes

### Task 13: Manual integration test

- [ ] **Step 1: Restart Aspire**
- [ ] **Step 2: Verify** Foundry Local resource appears in Aspire dashboard, whisper model downloads
- [ ] **Step 3: Send voice message in Telegram** — verify transcription works
- [ ] **Step 4:** `git push origin v3`

---

## Summary

| Layer | Changes | Why |
|-------|---------|-----|
| **Core** | `WhisperModel` (base + 3 models), `IAudioTranscriptionService`, `IAudioConverter`, `FoundryLocalTranscriptionService` (HTTP-based), `AddWhisperProvider()` | Whisper is an AI capability — same layer as LLM config |
| **AppHost** | `.WithVoice2Text()` / `.WithVoice2Text<T>()`, Aspire Foundry Local resource + whisper deployment | Aspire manages Foundry Local lifecycle, clean declarative API |
| **Telegram** | Implements `IAudioConverter`, consumes `IAudioTranscriptionService` from DI | Telegram provides OGG converter, doesn't know about Whisper |
| **Packages** | `Aspire.Hosting.Azure.AIFoundry` in AppHost | Aspire manages model download/loading/health checks |

### Key API

```csharp
// AppHost — declare voice-to-text capability
var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>()
    .WithVoice2Text<WhisperLargeV3Turbo>()   // Aspire Foundry Local
    .WithOllama(o => o.WithGPUSupport());

// Or use default (highest priority model):
    .WithVoice2Text()

// Service project — register transcription service
builder.AddWhisperProvider();  // registers IAudioTranscriptionService

// Consume via DI
public class MyService(IAudioTranscriptionService transcription) { ... }
```
