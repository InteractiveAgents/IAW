# Ollama Embedding Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `WithEmbedding<T>()` builder chain to the Aspire hosting layer so local Ollama embedding models work end-to-end, and replace the crash-on-missing-key fallback with a no-op generator.

**Architecture:** Mirror the existing `WithLLM<T>()` pattern for embeddings. AppHost declares `WithEmbedding<MxbaiEmbedLarge>()` which registers the Ollama model resource, propagates config via env vars, and the silo-side `AddEmbeddingProvider()` reads that config to create the right client. When no embedding model is declared, register a `NoOpEmbeddingGenerator` instead of throwing.

**Tech Stack:** OllamaSharp (already implements `IEmbeddingGenerator<string, Embedding<float>>`), Microsoft.Extensions.AI, Aspire Hosting, Orleans

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Aspire.Hosting.IAW/IAWService.cs` | Modify | Add `DeclaredEmbeddingModel` property |
| `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs` | Modify | Add `WithEmbedding<T>()`, propagate embedding env vars in `WithReference()` |
| `src/Aspire.Hosting.IAW/LLMModelBuilder.cs` | Modify | Add `WithEmbedding<T>()` passthrough for chaining |
| `src/Aspire.IAW.Client/LlmRegistration.cs` | Modify | Rewrite `AddEmbeddingProvider()` to read declared model, support Ollama/OpenAI/GitHub, no-op fallback |
| `src/Core/AI/LlmConfig.cs` | Modify | Add embedding config key constants |
| `src/Core/AI/NoOpEmbeddingGenerator.cs` | Create | Returns zero vectors, used when no embedding model configured |
| `test/Core.Tests/EmbeddingModelTests.cs` | Modify | Add tests for Ollama embedding registration and no-op generator |
| `src/IAW.AppHost/AppHost.cs` | Modify | Add `.WithEmbedding<MxbaiEmbedLarge>()` to chain |

---

### Task 1: NoOpEmbeddingGenerator

**Files:**
- Create: `src/Core/AI/NoOpEmbeddingGenerator.cs`
- Test: `test/Core.Tests/EmbeddingModelTests.cs`

- [ ] **Step 1: Write failing test for NoOpEmbeddingGenerator**

Add to `test/Core.Tests/EmbeddingModelTests.cs`:

```csharp
[Fact]
public async Task NoOpEmbeddingGenerator_returns_zero_vectors()
{
    var generator = new NoOpEmbeddingGenerator();
    var result = await generator.GenerateAsync(["hello", "world"]);
    Assert.Equal(2, result.Count);
    Assert.All(result, e => Assert.True(e.Vector.Span.ToArray().All(f => f == 0f)));
}

[Fact]
public async Task NoOpEmbeddingGenerator_returns_configurable_dimensions()
{
    var generator = new NoOpEmbeddingGenerator(768);
    var result = await generator.GenerateAsync(["test"]);
    Assert.Equal(768, result[0].Vector.Length);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~NoOpEmbeddingGenerator" -v minimal`
Expected: FAIL — `NoOpEmbeddingGenerator` does not exist

- [ ] **Step 3: Implement NoOpEmbeddingGenerator**

Create `src/Core/AI/NoOpEmbeddingGenerator.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace Core.AI;

public sealed class NoOpEmbeddingGenerator(int dimensions = 384) : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata => new("no-op");

    public object? GetService(Type serviceType, object? key = null) => null;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(_ => new Embedding<float>(new float[dimensions])).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public void Dispose() { }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~NoOpEmbeddingGenerator" -v minimal`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Core/AI/NoOpEmbeddingGenerator.cs test/Core.Tests/EmbeddingModelTests.cs
git commit -m "feat: add NoOpEmbeddingGenerator for graceful degradation without cloud embeddings"
```

---

### Task 2: Add embedding config constants

**Files:**
- Modify: `src/Core/AI/LlmConfig.cs`

- [ ] **Step 1: Add embedding config keys**

Add these constants to the `LlmConfig` class in `src/Core/AI/LlmConfig.cs`:

```csharp
public const string EmbeddingModelId = "AI:Embedding:ModelId";
public const string EmbeddingProvider = "AI:Embedding:Provider";
public const string EmbeddingServiceKey = "AI:Embedding:ServiceKey";
public const string EmbeddingDimensions = "AI:Embedding:Dimensions";
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Core/AI/LlmConfig.cs
git commit -m "feat: add embedding model config key constants"
```

---

### Task 3: AppHost-side WithEmbedding\<T\>() and env propagation

**Files:**
- Modify: `src/Aspire.Hosting.IAW/IAWService.cs`
- Modify: `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs`
- Modify: `src/Aspire.Hosting.IAW/LLMModelBuilder.cs`

- [ ] **Step 1: Add DeclaredEmbeddingModel to IAWService**

In `src/Aspire.Hosting.IAW/IAWService.cs`, add this property after line 16 (`WhisperModel`):

```csharp
internal EmbeddingModel? DeclaredEmbeddingModel { get; set; }
```

- [ ] **Step 2: Add WithEmbedding\<T\>() to IAWHostingExtensions**

In `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs`, add after the `WithVoice2Text<TModel>()` method (after line 89):

```csharp
public static IAWService WithEmbedding<TModel>(this IAWService iaw)
    where TModel : EmbeddingModel
{
    EmbeddingModel.EnsureAllModelsLoaded();
    var model = EmbeddingModel.All.OfType<TModel>().First();

    iaw.DeclaredEmbeddingModel = model;

    if (model.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
    {
        iaw.OllamaResource ??= iaw.AppBuilder.AddOllama("ollama");
        var modelResource = iaw.OllamaResource.AddModel(model.Id);
        iaw.OllamaModelResources.Add(modelResource);
    }

    return iaw;
}
```

- [ ] **Step 3: Propagate embedding env vars in WithReference()**

In the `WithReference<T>(builder, IAWService iaw)` method of `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs`, add after the whisper env var block (after line 170):

```csharp
if (iaw.DeclaredEmbeddingModel is { } embeddingModel)
{
    builder.WithEnvironment("AI__Embedding__ModelId", embeddingModel.Id);
    builder.WithEnvironment("AI__Embedding__Provider", embeddingModel.Provider);
    builder.WithEnvironment("AI__Embedding__ServiceKey", embeddingModel.ServiceKey);
    builder.WithEnvironment("AI__Embedding__Dimensions", embeddingModel.Dimensions.ToString());
}
```

- [ ] **Step 4: Add WithEmbedding\<T\>() passthrough to LLMModelBuilder**

In `src/Aspire.Hosting.IAW/LLMModelBuilder.cs`, add after the `WithVoice2Text<TModel>()` method (after line 40):

```csharp
public IAWService WithEmbedding<TModel>() where TModel : EmbeddingModel
    => IAW.WithEmbedding<TModel>();
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add src/Aspire.Hosting.IAW/IAWService.cs src/Aspire.Hosting.IAW/IAWHostingExtensions.cs src/Aspire.Hosting.IAW/LLMModelBuilder.cs
git commit -m "feat: add WithEmbedding<T>() builder chain and env propagation"
```

---

### Task 4: Rewrite AddEmbeddingProvider() silo-side

**Files:**
- Modify: `src/Aspire.IAW.Client/LlmRegistration.cs`

- [ ] **Step 1: Replace AddEmbeddingProvider() method**

Replace the entire `AddEmbeddingProvider()` method (lines 185-215) in `src/Aspire.IAW.Client/LlmRegistration.cs` with:

```csharp
internal static IHostApplicationBuilder AddEmbeddingProvider(this IHostApplicationBuilder builder)
{
    var config = builder.Configuration;

    var declaredProvider = config[LlmConfig.EmbeddingProvider];
    var declaredModelId = config[LlmConfig.EmbeddingModelId];

    if (string.Equals(declaredProvider, "ollama", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(declaredModelId))
    {
        var modelConnectionString = FindOllamaModelConnectionString(config,
            declaredModelId.Replace(".", "-").Replace(":", "-"));
        var endpoint = ParseOllamaEndpoint(modelConnectionString)
            ?? config[LlmConfig.OllamaEndpoint]
            ?? config["ConnectionStrings:ollama"]
            ?? "http://localhost:11434";

        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new OllamaApiClient(new Uri(endpoint), declaredModelId));
    }
    else if (!string.IsNullOrEmpty(config[LlmConfig.GitHubModelsApiKey]))
    {
        var token = config[LlmConfig.GitHubModelsApiKey]!;
        var modelId = declaredModelId ?? "text-embedding-3-small";
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            _ => new OpenAI.OpenAIClient(
                    new ApiKeyCredential(token),
                    new OpenAI.OpenAIClientOptions { Endpoint = new Uri(LlmConfig.GitHubModelsEndpoint) })
                .GetEmbeddingClient(modelId)
                .AsIEmbeddingGenerator());
    }
    else if (!string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]))
    {
        var apiKey = config[LlmConfig.OpenAiApiKey]!;
        var modelId = declaredModelId ?? "text-embedding-3-small";
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            _ => new OpenAI.OpenAIClient(apiKey)
                .GetEmbeddingClient(modelId)
                .AsIEmbeddingGenerator());
    }
    else
    {
        var dimensions = int.TryParse(config[LlmConfig.EmbeddingDimensions], out var d) ? d : 384;
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new NoOpEmbeddingGenerator(dimensions));
    }

    return builder;
}
```

- [ ] **Step 2: Add using for Core.AI at top of file if missing**

Verify `using Core.AI;` is at the top of `src/Aspire.IAW.Client/LlmRegistration.cs`. It should already be there.

- [ ] **Step 3: Extract helper for Ollama connection string lookup by sanitized ID**

The existing `FindOllamaModelConnectionString` takes a `LLMModel`. Add an overload that takes a pre-sanitized string. Add after the existing method (around line 141):

```csharp
private static string? FindOllamaModelConnectionString(IConfiguration config, string sanitizedId)
{
    return config[$"ConnectionStrings:ollama-{sanitizedId}"];
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/Aspire.IAW.Client/Aspire.IAW.Client.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Aspire.IAW.Client/LlmRegistration.cs
git commit -m "feat: rewrite AddEmbeddingProvider to support Ollama, no-op fallback"
```

---

### Task 5: Update AppHost to use WithEmbedding

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs`

- [ ] **Step 1: Add WithEmbedding\<MxbaiEmbedLarge\>() to the chain**

In `src/IAW.AppHost/AppHost.cs`, add `.WithEmbedding<MxbaiEmbedLarge>()` after `.WithLLM<Qwen25_7B>()` in the local-only section. The chain should look like:

```csharp
var iaw = builder.AddIAW("iaw")
    //.WithLLM<Gpt54Mini>().AsBalanced()
    //.WithLLM<Qwen25_7B>()
    //.WithLLM<Claude45Haiku>()
    //.WithLLM<Gpt54Nano>().AsFast()
    //.WithLLM<Sonnet46>()
    //.WithLLM<Opus46>().AsReasoning()
    //.WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text<WhisperLargeV3Turbo>()
    // Local-only mode (3060 Ti / 8GB VRAM) — all tiers auto-fallback to the single model:
    .WithLLM<Qwen25_7B>()
    .WithEmbedding<MxbaiEmbedLarge>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());
```

- [ ] **Step 2: Build AppHost to verify**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat: add WithEmbedding<MxbaiEmbedLarge>() for local Ollama embeddings"
```

---

### Task 6: Integration tests

**Files:**
- Modify: `test/Core.Tests/EmbeddingModelTests.cs`

- [ ] **Step 1: Add tests for embedding model discovery and service key**

Add to `test/Core.Tests/EmbeddingModelTests.cs`:

```csharp
[Fact]
public void MxbaiEmbedLarge_has_correct_service_key()
{
    var model = EmbeddingModel.All.First(m => m is MxbaiEmbedLarge);
    Assert.Equal("ollama-mxbai-embed-large", model.ServiceKey);
    Assert.True(model.IsLocal);
}

[Fact]
public void TextEmbedding3Small_has_correct_service_key()
{
    var model = EmbeddingModel.All.First(m => m is TextEmbedding3Small);
    Assert.Equal("openai-text-embedding-3-small", model.ServiceKey);
    Assert.False(model.IsLocal);
}
```

- [ ] **Step 2: Run all embedding tests**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~EmbeddingModel" -v minimal`
Expected: All pass (5 existing + 2 new NoOp + 2 new service key = 9 total)

- [ ] **Step 3: Run full test suite**

Run: `dotnet test IAW.slnx -v minimal`
Expected: All pass, no regressions

- [ ] **Step 4: Commit**

```bash
git add test/Core.Tests/EmbeddingModelTests.cs
git commit -m "test: add embedding model service key and NoOp generator tests"
```

---

### Task 7: End-to-end verification

- [ ] **Step 1: Start Aspire**

Run AppHost via Aspire and verify:
1. Ollama container starts with both `qwen2.5:7b` and `mxbai-embed-large` models
2. Assistant resource shows `AI__Embedding__*` environment variables
3. No "No embedding provider configured" exception in logs
4. Memory agents activate without errors

- [ ] **Step 2: Test embedding via MCP or DevUI**

Send a message through Telegram or DevUI that triggers memory storage (the memory agents use embeddings). Verify no exceptions in Aspire traces.

- [ ] **Step 3: Verify no-op fallback works**

Temporarily comment out `.WithEmbedding<MxbaiEmbedLarge>()` in AppHost, rebuild and restart. Verify assistant starts without errors — no-op generator should kick in instead of throwing.
