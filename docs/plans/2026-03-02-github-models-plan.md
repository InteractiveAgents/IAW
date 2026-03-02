# GitHub Models Integration — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add GitHub Models as an LLM provider so agents can use GPT-4o and GPT-4o-mini via GitHub's OpenAI-compatible inference endpoint, reusing the existing `IChatClient` / `[Llm<>]` infrastructure.

**Architecture:** GitHub Models exposes an OpenAI-compatible API at `https://models.inference.ai.azure.com`. We reuse the existing `OpenAI` NuGet SDK with a custom endpoint and the GitHub PAT as the API key. No new packages needed. A new `ProviderType.GitHub` enum value threads through `LlmConfig`, `LlmRegistration`, and `IAWExtensions`.

**Tech Stack:** OpenAI .NET SDK (existing), Microsoft.Extensions.AI (existing), Orleans 10.0, .NET Aspire

---

### Task 1: Add `GitHub` to ProviderType enum

**Files:**
- Modify: `src/Core/AI/ProviderType.cs:5-7`

**Step 1: Add the enum value**

```csharp
public enum ProviderType
{
    Ollama,
    Anthropic,
    OpenAI,
    GitHub
}
```

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Core/AI/ProviderType.cs
git commit -m "feat: add GitHub to ProviderType enum"
```

---

### Task 2: Add GitHub config constants to LlmConfig

**Files:**
- Modify: `src/Core/AI/LlmConfig.cs:4-8`

**Step 1: Add constants**

```csharp
public static class LlmConfig
{
    public const string AnthropicApiKey = "AI:LLM:AnthropicApiKey";
    public const string OpenAiApiKey = "AI:LLM:OpenAiApiKey";
    public const string OllamaEndpoint = "AI:LLM:OllamaEndpoint";
    public const string GitHubToken = "GitHub:Token";
    public const string GitHubModelsApiKey = "AI:LLM:GitHubToken";
    public const string GitHubModelsEndpoint = "https://models.inference.ai.azure.com";
}
```

`GitHubToken` stays for Octokit. `GitHubModelsApiKey` maps to the env var Aspire injects (`AI__LLM__GitHubToken`). `GitHubModelsEndpoint` is the fixed inference endpoint.

**Step 2: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/Core/AI/LlmConfig.cs
git commit -m "feat: add GitHub Models config constants"
```

---

### Task 3: Create GitHubGpt4oMini and GitHubGpt4o model definitions

**Files:**
- Create: `src/Core/AI/Models/GitHubGpt4oMini.cs`
- Create: `src/Core/AI/Models/GitHubGpt4o.cs`
- Modify: `src/Core/AI/LLMModel.cs:36-44` (EnsureAllModelsLoaded)

**Step 1: Create GitHubGpt4oMini.cs**

```csharp
namespace Core.AI.Models;

public sealed class GitHubGpt4oMini : LLMModel
{
    public static readonly GitHubGpt4oMini Instance = new();
    private GitHubGpt4oMini() { }

    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GitHub GPT-4o Mini";
    public override ProviderType Provider => ProviderType.GitHub;
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}
```

**Step 2: Create GitHubGpt4o.cs**

```csharp
namespace Core.AI.Models;

public sealed class GitHubGpt4o : LLMModel
{
    public static readonly GitHubGpt4o Instance = new();
    private GitHubGpt4o() { }

    public override string Id => "gpt-4o";
    public override string DisplayName => "GitHub GPT-4o";
    public override ProviderType Provider => ProviderType.GitHub;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
```

Note: These have the same model IDs as OpenAI models (`gpt-4o-mini`, `gpt-4o`) but different `Provider = ProviderType.GitHub`. The `ServiceKey` property auto-derives `github-gpt-4o-mini` and `github-gpt-4o` — different from `openai-gpt-4o-mini`.

**Step 3: Register in EnsureAllModelsLoaded**

In `src/Core/AI/LLMModel.cs`, update `EnsureAllModelsLoaded()`:

```csharp
public static void EnsureAllModelsLoaded()
{
    _ = Models.Claude45Haiku.Instance;
    _ = Models.Sonnet46.Instance;
    _ = Models.Gpt4o.Instance;
    _ = Models.Gpt4oMini.Instance;
    _ = Models.Llama32.Instance;
    _ = Models.Qwen25.Instance;
    _ = Models.GitHubGpt4oMini.Instance;
    _ = Models.GitHubGpt4o.Instance;
}
```

**Step 4: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add src/Core/AI/Models/GitHubGpt4oMini.cs src/Core/AI/Models/GitHubGpt4o.cs src/Core/AI/LLMModel.cs
git commit -m "feat: add GitHubGpt4oMini and GitHubGpt4o model definitions"
```

---

### Task 4: Wire GitHub provider into LlmRegistration

**Files:**
- Modify: `src/Core/AI/LlmRegistration.cs:1` (add using)
- Modify: `src/Core/AI/LlmRegistration.cs:78-88` (IsProviderConfigured)
- Modify: `src/Core/AI/LlmRegistration.cs:98-106` (CreateChatClient switch)
- Add new method after line 164: `CreateGitHubModelsClient`

**Step 1: Add using for System.ClientModel**

At top of file, add:
```csharp
using System.ClientModel;
```

**Step 2: Add GitHub case to IsProviderConfigured**

```csharp
public static bool IsProviderConfigured(IConfiguration config, ProviderType provider)
{
    return provider switch
    {
        ProviderType.Ollama => !string.IsNullOrEmpty(config[LlmConfig.OllamaEndpoint])
                               || !string.IsNullOrEmpty(config["ConnectionStrings:ollama"])
                               || HasOllamaModelConnectionString(config),
        ProviderType.Anthropic => !string.IsNullOrEmpty(config[LlmConfig.AnthropicApiKey]),
        ProviderType.OpenAI => !string.IsNullOrEmpty(config[LlmConfig.OpenAiApiKey]),
        ProviderType.GitHub => !string.IsNullOrEmpty(config[LlmConfig.GitHubModelsApiKey]),
        _ => false
    };
}
```

**Step 3: Add GitHub case to CreateChatClient switch**

```csharp
var innerClient = model.Provider switch
{
    ProviderType.Ollama => CreateOllamaClient(config, model),
    ProviderType.Anthropic => CreateAnthropicClient(config, model),
    ProviderType.OpenAI => CreateOpenAiClient(config, model),
    ProviderType.GitHub => CreateGitHubModelsClient(config, model),
    _ => throw new NotSupportedException($"Provider {model.Provider} not supported")
};
```

**Step 4: Add CreateGitHubModelsClient method**

Add after `CreateOpenAiClient` method:

```csharp
private static IChatClient CreateGitHubModelsClient(IConfiguration config, LLMModel model)
{
    var token = config[LlmConfig.GitHubModelsApiKey]
        ?? throw new InvalidOperationException("GitHub token not configured for GitHub Models.");
    return new OpenAI.OpenAIClient(
            new ApiKeyCredential(token),
            new OpenAI.OpenAIClientOptions { Endpoint = new Uri(LlmConfig.GitHubModelsEndpoint) })
        .GetChatClient(model.Id)
        .AsIChatClient();
}
```

**Step 5: Build to verify**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded

**Step 6: Commit**

```bash
git add src/Core/AI/LlmRegistration.cs
git commit -m "feat: wire GitHub Models provider into LlmRegistration"
```

---

### Task 5: Make GitHub token injection conditional in IAWExtensions

**Files:**
- Modify: `src/IAW.AppHost/IAWExtensions.cs:97-99`

**Step 1: Replace unconditional GitHub token injection with conditional**

Replace lines 98-99:

```csharp
_gitHubTokenParam ??= appBuilder.AddParameter("github-token", secret: true);
builder.WithEnvironment("AI__LLM__GitHubToken", _gitHubTokenParam);
```

With:

```csharp
if (_declaredProviders.Contains(ProviderType.GitHub))
{
    _gitHubTokenParam ??= appBuilder.AddParameter("github-token", secret: true);
    builder.WithEnvironment("AI__LLM__GitHubToken", _gitHubTokenParam);
}
```

Note: The Octokit `GitHubService` reads from `GitHub:Token` config key (not `AI:LLM:GitHubToken`), so this change does NOT break the existing GitHub client integration. Octokit gets its token from a different path. If you also want `GitHub:Token` injected for Octokit, that's separate from LLM config and not changed here.

**Step 2: Build to verify**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/IAW.AppHost/IAWExtensions.cs
git commit -m "refactor: make GitHub token injection conditional on GitHub provider"
```

---

### Task 6: Add WithLLM<GitHubGpt4oMini> to AppHost

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs:6-9`

**Step 1: Add GitHubGpt4oMini to the IAW builder chain**

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Claude45Haiku>()
    .WithLLM<GitHubGpt4oMini>()
    .WithLLM<Qwen25>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());
```

Also add the using at the top if not already globalized — Core.AI.Models is already imported on line 2.

**Step 2: Build to verify**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat: declare GitHubGpt4oMini in AppHost"
```

---

### Task 7: Create GitHubTestAgent grain and sample endpoint

**Files:**
- Create: `samples/Samples/GitHubTestAgent.cs`
- Modify: `samples/Samples/Program.cs:26-28` (add `AddLlmProviders` + new endpoint)

**Step 1: Create GitHubTestAgent grain**

```csharp
using Core;
using Core.AI;
using Core.AI.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Samples;

public sealed class GitHubTestAgent(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking,
    [Llm<GitHubGpt4oMini>] IChatClient chatClient)
    : Agent(values, history, events, subscriptions, notifications, tracking)
{
    public override string DisplayName => "GitHub Test Agent";
    public override string SystemPrompt => "You are a helpful test agent. Keep responses under 50 words.";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        Activate(chatClient);
    }
}
```

**Step 2: Add AddLlmProviders to Samples Program.cs**

In `samples/Samples/Program.cs`, add after line 26 (`builder.AddGitHubClient();`):

```csharp
builder.AddLlmProviders();
```

This requires adding `using Core.AI;` at the top of the file.

**Step 3: Add /github-models endpoint**

Add after the `app.MapDefaultEndpoints();` line (line 30):

```csharp
app.MapGet("/samples/github-models", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"github-test-{Guid.NewGuid():N}");
    var chunks = new List<string>();
    await foreach (var chunk in agent.SendAsync("Say hello in one sentence.", ct))
        chunks.Add(chunk);

    var response = string.Join("", chunks);
    return Results.Ok(new
    {
        model = "gpt-4o-mini",
        provider = "GitHub",
        response,
        hasContent = !string.IsNullOrWhiteSpace(response)
    });
});
```

Wait — `SendAsync` is on the Agent class, not on `IAgent`. The grain interface doesn't expose `SendAsync` because it returns `IAsyncEnumerable<string>`. Let me check what IAgent exposes for LLM interaction...

Actually, `SendAsync` is defined directly on the `Agent` class (line 410) and is NOT part of the `IAgent` interface. So calling it via `IGrainFactory.GetGrain<IAgent>()` won't work. We need a different approach.

Instead, use the agent's history behavior: send a message via `AddHistoryAsync`, and the agent will need an explicit method. OR, create a separate grain interface.

Simpler approach: don't exercise `SendAsync` from the HTTP endpoint. Instead, just verify the agent activates correctly (meaning the `[Llm<>]` injection worked) and use the agent's state behavior to confirm. Then add a dedicated interface or just call `GetMetadataAsync` to confirm the grain activated with its LLM.

**Revised Step 3: Simpler /github-models endpoint**

```csharp
app.MapGet("/samples/github-models", async (IGrainFactory grains, CancellationToken ct) =>
{
    var agent = grains.GetGrain<IAgent>($"github-test-{Guid.NewGuid():N}");
    var metadata = await agent.GetMetadataAsync(ct);

    return Results.Ok(new
    {
        model = "gpt-4o-mini",
        provider = "GitHub",
        agentId = metadata.Id,
        displayName = metadata.DisplayName,
        activated = true
    });
});
```

This verifies the grain activates successfully (meaning `[Llm<GitHubGpt4oMini>]` injection resolved correctly). If the IChatClient isn't registered, activation will throw.

**Step 4: Build to verify**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded (full solution)

**Step 5: Commit**

```bash
git add samples/Samples/GitHubTestAgent.cs samples/Samples/Program.cs
git commit -m "feat: add GitHubTestAgent grain and /github-models sample endpoint"
```

---

### Task 8: Run Aspire and verify end-to-end

**Step 1: Start the application**

Run: `aspire run`

You will be prompted for the `github-token` secret parameter if not already configured. Enter a valid GitHub PAT with `models:read` scope.

**Step 2: Hit the sample endpoint**

Run: `curl http://localhost:<samples-port>/samples/github-models`

Expected: JSON with `activated: true`, `displayName: "GitHub Test Agent"`

**Step 3: Run all tests**

Run: `dotnet test IAW.slnx`

Expected: All tests pass (existing tests don't touch GitHub Models — they use MockChatClient)

**Step 4: Commit (if any fixups were needed)**

```bash
git add -A
git commit -m "fix: address any issues from end-to-end verification"
```

---

### Task 9: Run integration tests

**Step 1: Run integration tests specifically**

Run: `dotnet test test/Integration.Tests/IAW.Integration.Tests.csproj`

Expected: All green. Integration tests use Aspire's `DistributedApplicationTestingBuilder` so they will exercise the full AppHost including the new `WithLLM<GitHubGpt4oMini>()` declaration. If `github-token` is not set, the `GitHubGpt4oMini` provider simply won't register (graceful skip via `IsProviderConfigured`).

**Step 2: Run unit tests**

Run: `dotnet test test/Core.Tests/IAW.Core.Tests.csproj`

Expected: All 41 tests pass. Unit tests use `AgentTest<Agent>` which uses the base `Agent` class without LLM injection.
