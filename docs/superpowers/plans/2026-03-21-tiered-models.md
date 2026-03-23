# Tiered Model Abstraction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable agents to request LLM capability tiers (`Fast`, `Balanced`, `Reasoning`) instead of concrete models, with AppHost mapping concrete models to tiers.

**Architecture:** Three `LLMModel` subtypes with synthetic `"tier"` provider identity survive auto-discovery. `WithLLM<T>()` returns a `LLMModelBuilder` enabling `.AsFast()` chaining. Tiers propagate as env vars and resolve to aliased `IChatClient` keyed services on the silo.

**Tech Stack:** Orleans, Microsoft.Extensions.AI, Aspire Hosting, xunit.v3

**Spec:** `docs/superpowers/specs/2026-03-21-tiered-models-and-rich-rendering-design.md` (Phase 1)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `src/Core/AI/ModelTiers.cs` | Create | `Fast`, `Balanced`, `Reasoning` marker types with `"tier"` provider |
| `src/Aspire.Hosting.IAW/LLMModelBuilder.cs` | Create | Intermediate builder returned by `WithLLM<T>()` for type-safe `.AsFast()` chaining |
| `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs` | Modify:38-62 | `WithLLM<T>()` returns `LLMModelBuilder`; propagate tier env vars in `WithReference` |
| `src/Aspire.IAW.Client/LlmRegistration.cs` | Modify:15-56 | Skip `Provider == "tier"` in concrete model creation; read tier env vars and register keyed aliases |
| `src/IAW.AppHost/AppHost.cs` | Modify:5-15 | Use new tier syntax on existing WithLLM chain |
| `test/Core.Tests/ModelTierTests.cs` | Create | Tests for tier types, auto-discovery, service key resolution |
| `test/Core.Tests/TierRegistrationTests.cs` | Create | Tests for DI alias registration and fallback behavior |

---

### Task 1: Tier Marker Types

**Files:**
- Create: `src/Core/AI/ModelTiers.cs`
- Create: `test/Core.Tests/ModelTierTests.cs`

- [ ] **Step 1: Write failing tests**

Create `test/Core.Tests/ModelTierTests.cs`:

```csharp
using Core.AI;
using Xunit;

namespace IAW.Core.Tests;

public class ModelTierTests
{
    [Fact]
    public void Fast_HasValidServiceKey()
    {
        var fast = LLMModel.All.First(m => m.GetType() == typeof(Fast));
        Assert.Equal("tier-tier-fast", fast.ServiceKey);
    }

    [Fact]
    public void Balanced_HasValidServiceKey()
    {
        var balanced = LLMModel.All.First(m => m.GetType() == typeof(Balanced));
        Assert.Equal("tier-tier-balanced", balanced.ServiceKey);
    }

    [Fact]
    public void Reasoning_HasValidServiceKey()
    {
        var reasoning = LLMModel.All.First(m => m.GetType() == typeof(Reasoning));
        Assert.Equal("tier-tier-reasoning", reasoning.ServiceKey);
    }

    [Fact]
    public void TierTypes_SurviveAutoDiscovery()
    {
        var all = LLMModel.All;
        Assert.Contains(all, m => m is Fast);
        Assert.Contains(all, m => m is Balanced);
        Assert.Contains(all, m => m is Reasoning);
    }

    [Fact]
    public void TierTypes_HaveTierProvider()
    {
        var tiers = LLMModel.All.Where(m => m.Provider == "tier").ToList();
        Assert.Equal(3, tiers.Count);
    }

    [Fact]
    public void TierTypes_DoNotInterfereWithConcreteModels()
    {
        var concreteCount = LLMModel.All.Count(m => m.Provider != "tier");
        Assert.True(concreteCount > 0);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ModelTierTests" -v m`
Expected: FAIL -- `Fast` type does not exist

- [ ] **Step 3: Create tier marker types**

Create `src/Core/AI/ModelTiers.cs`:

```csharp
namespace Core.AI;

public sealed class Fast : LLMModel
{
    internal Fast() : base("tier-fast", "tier", "Fast") { }
}

public sealed class Balanced : LLMModel
{
    internal Balanced() : base("tier-balanced", "tier", "Balanced") { }
}

public sealed class Reasoning : LLMModel
{
    internal Reasoning() : base("tier-reasoning", "tier", "Reasoning") { }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~ModelTierTests" -v m`
Expected: All 6 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/Core/AI/ModelTiers.cs test/Core.Tests/ModelTierTests.cs
git commit -m "feat: add Fast, Balanced, Reasoning tier marker types"
```

---

### Task 2: LLMModelBuilder for Type-Safe Tier Chaining

**Files:**
- Create: `src/Aspire.Hosting.IAW/LLMModelBuilder.cs`
- Modify: `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs:38-62`
- Modify: `src/Aspire.Hosting.IAW/IAWService.cs`

- [ ] **Step 1: Add TierMappings to IAWService**

Read `src/Aspire.Hosting.IAW/IAWService.cs`. Add a new property after `DeclaredProviders` (line 13):

```csharp
internal Dictionary<string, string> TierMappings { get; } = [];
```

This stores `{ tierServiceKey -> concreteModelServiceKey }`.

- [ ] **Step 2: Create LLMModelBuilder**

Create `src/Aspire.Hosting.IAW/LLMModelBuilder.cs`:

```csharp
using Core.AI;

namespace Aspire.Hosting;

public sealed class LLMModelBuilder(IAWService iaw, LLMModel lastModel)
{
    public LLMModelBuilder WithLLM<TModel>() where TModel : LLMModel
    {
        // delegate to the extension method, which returns a new LLMModelBuilder
        return IAWHostingExtensions.WithLLM<TModel>(iaw);
    }

    public LLMModelBuilder AsFast()
    {
        iaw.TierMappings[LLMModel.All.First(m => m is Fast).ServiceKey] = lastModel.ServiceKey;
        return this;
    }

    public LLMModelBuilder AsBalanced()
    {
        iaw.TierMappings[LLMModel.All.First(m => m is Balanced).ServiceKey] = lastModel.ServiceKey;
        return this;
    }

    public LLMModelBuilder AsReasoning()
    {
        iaw.TierMappings[LLMModel.All.First(m => m is Reasoning).ServiceKey] = lastModel.ServiceKey;
        return this;
    }

    // forward all other IAWService extension methods so the chain continues
    public IAWService WithOllama(Action<Aspire.Hosting.ApplicationModel.IResourceBuilder<CommunityToolkit.Aspire.Hosting.Ollama.OllamaResource>> configure)
        => iaw.WithOllamaInternal(configure);

    public IAWService WithVoice2Text<TModel>() where TModel : WhisperModel
        => IAWHostingExtensions.WithVoice2Text<TModel>(iaw);

    public IAWService WithVoice2Text()
        => IAWHostingExtensions.WithVoice2Text(iaw);

    public IAWService WithWorkspace(string path)
        => IAWHostingExtensions.WithWorkspace(iaw, path);

    // implicit conversion so chains without .As*() still work
    public static implicit operator IAWService(LLMModelBuilder b) => b.iaw;
}
```

Note: The exact forwarding methods depend on what extension methods exist on `IAWService`. Read `IAWHostingExtensions.cs` to see all methods and forward them. The key ones are `WithOllama`, `WithVoice2Text`, `WithWorkspace`, `WithStorage`, `WithVectorDb`, `WithCosmosStorage`.

- [ ] **Step 3: Change WithLLM to return LLMModelBuilder**

In `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs`, change the `WithLLM<TModel>` method (line 38) to return `LLMModelBuilder` instead of `IAWService`:

```csharp
public static LLMModelBuilder WithLLM<TModel>(this IAWService iaw)
    where TModel : LLMModel
{
    LLMModel.EnsureAllModelsLoaded();
    var model = LLMModel.All.OfType<TModel>().First();

    iaw.DeclaredModels.Add(model);
    iaw.DeclaredProviders.Add(model.Provider);

    if (model.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
    {
        iaw.OllamaResource ??= iaw.AppBuilder.AddOllama("ollama");
        var modelResource = iaw.OllamaResource.AddModel(model.Id);
        iaw.OllamaModelResources.Add(modelResource);
    }

    if (model.Provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        iaw.AnthropicKeyParam ??= iaw.AppBuilder.AddParameter("anthropic-api-key", secret: true)
            .WithDescription("Get your key at [console.anthropic.com/settings/keys](https://console.anthropic.com/settings/keys)", enableMarkdown: true);

    if (model.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase))
        iaw.OpenAiKeyParam ??= iaw.AppBuilder.AddParameter("openai-api-key", secret: true)
            .WithDescription("Get your key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys)", enableMarkdown: true);

    return new LLMModelBuilder(iaw, model);
}
```

Also add an overload so `LLMModelBuilder` can chain `WithLLM<T>()`:

```csharp
internal static LLMModelBuilder WithLLM<TModel>(IAWService iaw)
    where TModel : LLMModel
{
    return WithLLM<TModel>(iaw);  // calls the extension method
}
```

Wait -- that would be recursive. Instead, the `LLMModelBuilder.WithLLM<T>()` should call the extension method directly:

```csharp
// In LLMModelBuilder
public LLMModelBuilder WithLLM<TModel>() where TModel : LLMModel
    => IAWHostingExtensions.WithLLM<TModel>(iaw);
```

This works because `IAWHostingExtensions.WithLLM<TModel>(IAWService)` is an extension method that can be called as a static method.

- [ ] **Step 4: Propagate tier env vars in WithReference**

In `IAWHostingExtensions.cs`, in the `WithReference<T>` method (around line 119), after the model env var loop (line 131-138), add tier propagation:

```csharp
foreach (var (tierKey, concreteKey) in iaw.TierMappings)
{
    var tierName = tierKey.Replace("tier-tier-", "");  // "fast", "balanced", "reasoning"
    builder.WithEnvironment($"AI__LLM__Tiers__{tierName}", concreteKey);
}
```

- [ ] **Step 5: Build to verify compilation**

Run: `dotnet build src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj`
Expected: Build succeeded

Note: The `WithLLM<T>()` return type change from `IAWService` to `LLMModelBuilder` may break the AppHost compilation since it chains `.WithOllama()`, `.WithVoice2Text()`, etc. after `WithLLM`. The `LLMModelBuilder` has forwarding methods and implicit conversion to handle this. If compilation fails, add missing forwarding methods.

- [ ] **Step 6: Commit**

```bash
git add src/Aspire.Hosting.IAW/LLMModelBuilder.cs src/Aspire.Hosting.IAW/IAWHostingExtensions.cs src/Aspire.Hosting.IAW/IAWService.cs
git commit -m "feat: add LLMModelBuilder for type-safe tier chaining"
```

---

### Task 3: Silo Tier Alias Registration

**Files:**
- Modify: `src/Aspire.IAW.Client/LlmRegistration.cs:15-56`
- Create: `test/Core.Tests/TierRegistrationTests.cs`

- [ ] **Step 1: Write failing test**

Create `test/Core.Tests/TierRegistrationTests.cs`:

```csharp
using Core.AI;
using IAW.Testing;
using IAW.Agents.Orchestration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IAW.Core.Tests;

public class TierRegistrationTests : AgentTest<ThreadAgent>
{
    [Fact]
    public void MockClient_RegisteredForTierServiceKeys()
    {
        var fastKey = LLMModel.All.First(m => m is Fast).ServiceKey;
        var balancedKey = LLMModel.All.First(m => m is Balanced).ServiceKey;
        var reasoningKey = LLMModel.All.First(m => m is Reasoning).ServiceKey;

        // AgentTest registers mock client for ALL models in LLMModel.All
        // Tiers are in LLMModel.All, so they should have mock clients
        var fastClient = Cluster.ServiceProvider.GetKeyedService<IChatClient>(fastKey);
        var balancedClient = Cluster.ServiceProvider.GetKeyedService<IChatClient>(balancedKey);
        var reasoningClient = Cluster.ServiceProvider.GetKeyedService<IChatClient>(reasoningKey);

        Assert.NotNull(fastClient);
        Assert.NotNull(balancedClient);
        Assert.NotNull(reasoningClient);
    }

    [Fact]
    public void LlmAttribute_ResolvesForTierTypes()
    {
        var fastAttr = new LlmAttribute<Fast>();
        var balancedAttr = new LlmAttribute<Balanced>();
        var reasoningAttr = new LlmAttribute<Reasoning>();

        Assert.Equal("tier-tier-fast", fastAttr.ServiceKey);
        Assert.Equal("tier-tier-balanced", balancedAttr.ServiceKey);
        Assert.Equal("tier-tier-reasoning", reasoningAttr.ServiceKey);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass (mock infrastructure already handles tiers)**

Run: `dotnet test test/Core.Tests --filter "FullyQualifiedName~TierRegistrationTests" -v m`
Expected: PASS -- because `RegisterAllAttributeMappers(services, mockClient)` already iterates `LLMModel.All` which includes tier types and registers mock clients for them.

If tests fail, debug and fix.

- [ ] **Step 3: Modify LlmRegistration to skip tiers and register aliases**

In `src/Aspire.IAW.Client/LlmRegistration.cs`, modify `AddLlmProviders`:

1. In the `foreach (var model in modelsToRegister)` loop (line 39), skip tier models:

```csharp
foreach (var model in modelsToRegister)
{
    if (model.Provider == "tier")
        continue;

    if (!IsProviderConfigured(factoryMap, config, model.Provider))
        continue;

    builder.Services.AddKeyedSingleton<IChatClient>(model.ServiceKey,
        (sp, key) => CreateChatClient(sp, factoryMap, config, model));
}
```

2. After the default model registration (after line 54), add tier alias registration:

```csharp
// register tier aliases from env vars
var tierNames = new[] { "Fast", "Balanced", "Reasoning" };
foreach (var tierName in tierNames)
{
    var concreteKey = config[$"AI:LLM:Tiers:{tierName}"];
    var tierModel = LLMModel.All.FirstOrDefault(m =>
        m.Provider == "tier" && m.DisplayName.Equals(tierName, StringComparison.OrdinalIgnoreCase));

    if (tierModel is null) continue;

    if (!string.IsNullOrEmpty(concreteKey))
    {
        builder.Services.AddKeyedSingleton<IChatClient>(tierModel.ServiceKey,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>(concreteKey));
    }
    else if (firstConfigured is not null)
    {
        // fallback: tier resolves to default model
        builder.Services.AddKeyedSingleton<IChatClient>(tierModel.ServiceKey,
            (sp, _) => sp.GetRequiredKeyedService<IChatClient>(firstConfigured.ServiceKey));
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Aspire.IAW.Client/Aspire.IAW.Client.csproj`
Expected: Build succeeded

- [ ] **Step 5: Run all tests**

Run: `dotnet test test/Core.Tests -v m`
Expected: All tests pass

- [ ] **Step 6: Commit**

```bash
git add src/Aspire.IAW.Client/LlmRegistration.cs test/Core.Tests/TierRegistrationTests.cs
git commit -m "feat: register tier IChatClient aliases from env vars in silo"
```

---

### Task 4: AppHost Tier Syntax

**Files:**
- Modify: `src/IAW.AppHost/AppHost.cs:5-15`

- [ ] **Step 1: Update AppHost to use tier syntax**

Read `src/IAW.AppHost/AppHost.cs` and change the `WithLLM` chain to assign tiers:

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Gpt54Mini>()
    .WithLLM<Claude45Haiku>().AsFast()
    .WithLLM<Gpt54Nano>()
    .WithLLM<Sonnet46>().AsBalanced()
    .WithLLM<Opus46>().AsReasoning()
    .WithLLM<Qwen25>()
    .WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text<WhisperLargeV3Turbo>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI())
    .WithWorkspace("D:\\IAW-Workspace");
```

Note: The chain still works because `LLMModelBuilder` has forwarding methods for `WithVoice2Text`, `WithOllama`, `WithWorkspace`, and an implicit conversion to `IAWService`. If this doesn't compile, fix the forwarding methods in `LLMModelBuilder`.

- [ ] **Step 2: Build the full solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded (file lock errors from running Aspire are OK)

- [ ] **Step 3: Run all tests**

Run: `dotnet test test/Core.Tests -v m`
Expected: All tests pass

- [ ] **Step 4: Commit**

```bash
git add src/IAW.AppHost/AppHost.cs
git commit -m "feat: assign Fast/Balanced/Reasoning tiers in AppHost"
```

---

### Task 5: Integration Verification

**Files:** None (verification only)

- [ ] **Step 1: Run full test suite**

Run: `dotnet test IAW.slnx -v m`
Expected: All tests pass

- [ ] **Step 2: Restart Aspire and verify**

Restart assistant and telegram resources. Check that:
- No startup errors in console logs
- Tier env vars are propagated (check resource environment variables in Aspire dashboard)
- Agents still function normally (send a test message via MCP or Telegram)

- [ ] **Step 3: Verify tier env vars in Aspire**

Use `mcp__aspire__list_resources` and check the `assistant` resource's environment variables include:
- `AI__LLM__Tiers__Fast`
- `AI__LLM__Tiers__Balanced`
- `AI__LLM__Tiers__Reasoning`

- [ ] **Step 4: Commit any fixups**

```bash
git add -A
git commit -m "fix: integration fixups for tiered model abstraction"
```
