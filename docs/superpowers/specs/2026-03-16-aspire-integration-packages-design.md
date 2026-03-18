# Aspire Integration Packages: Aspire.Hosting.IAW + Aspire.IAW.Client

**Date:** 2026-03-16
**Status:** Draft
**Scope:** Extract Aspire hosting and client integrations into proper packages following Aspire conventions

---

## Problem

The IAW silo startup (`IAW.Assistant/Program.cs`) requires 7 lines of manual registration:

```csharp
builder.AddServiceDefaults();
builder.AddIAW();
builder.AddLlmProviders();
builder.AddEmbeddingProvider();
builder.AddAzureBlobServiceClient("file-storage");
builder.AddQdrantClient("qdrant");
builder.Services.AddSingleton<BlobFileStorage>();
```

The AppHost (`AppHost.cs`) manually creates Azure Storage, Qdrant, and propagates environment variables via a separate `WithLLMEnvironment(builder)` call. Infrastructure concerns are scattered across multiple projects:

- `IAW.ServiceDefaults` — OTel, health checks, service discovery, Orleans silo/client setup
- `IAW.AppHost/IAWExtensions.cs` — Orleans service declaration, LLM model registration, env propagation
- `Core/AI/LlmRegistration.cs` — LLM provider factories, embedding provider, attribute mapper registration

Additionally, `IAWExtensions.cs` uses **static mutable state** (`_appBuilder`, `_declaredModels`, `_declaredProviders`, etc.) which is fragile and prevents multiple IAW services in one AppHost.

## Solution

Create two new packages following the standard Aspire integration pattern:

| Package | Role | Analogy |
|---------|------|---------|
| `Aspire.Hosting.IAW` | AppHost-side resource definition | `Aspire.Hosting.Qdrant` |
| `Aspire.IAW.Client` | Service-side client integration | `Aspire.Qdrant.Client` |

Delete `IAW.ServiceDefaults` entirely. Move `LlmRegistration` extension methods to the client package (keeping attribute mapper helpers accessible to `IAW.Testing`).

---

## Package: Aspire.Hosting.IAW

### Project

- **Path:** `src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj`
- **PackageId:** `Aspire.Hosting.IAW`
- **RootNamespace:** `Aspire.Hosting`
- **IsPackable:** true
- **Tags:** `aspire`, `hosting`, `integration`, `orleans`, `agents`, `ai`, `iaw`
- **Description:** `Aspire hosting integration for the IAW multi-agent runtime`

### Dependencies

- `Aspire.Hosting.Orleans`
- `CommunityToolkit.Aspire.Hosting.Ollama`
- `Aspire.Hosting.Azure.Storage`
- `Aspire.Hosting.Qdrant`
- `IAW.Core` (for `LLMModel`, `WhisperModel` types)

> **Note:** `Aspire.Hosting.Qdrant` also depends on `Qdrant.Client` and `CommunityToolkit.Aspire.Hosting.Ollama` depends on `OllamaSharp`. Taking a dependency on `IAW.Core` for the model type system follows the same pattern. If this becomes a concern post-1.0, the model type hierarchy can be extracted into a thin `IAW.Abstractions` package.

### IAWService

Central configuration object returned by `AddIAW()`. Replaces the static mutable state in the current `IAWExtensions.cs`. All state is instance-scoped, allowing multiple IAW services in one AppHost.

```csharp
public class IAWService(OrleansService orleans, IDistributedApplicationBuilder appBuilder)
{
    internal OrleansService Orleans { get; } = orleans;
    internal IDistributedApplicationBuilder AppBuilder { get; } = appBuilder;
    internal List<LLMModel> DeclaredModels { get; } = [];
    internal HashSet<string> DeclaredProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal IResourceBuilder<OllamaResource>? OllamaResource { get; set; }
    internal List<IResourceBuilder<OllamaModelResource>> OllamaModelResources { get; } = [];
    internal WhisperModel? WhisperModel { get; set; }

    // Infrastructure resources — created with defaults, configurable via callbacks
    internal IResourceBuilder<AzureStorageResource> Storage { get; set; } = null!;
    internal IResourceBuilder<AzureBlobStorageResource> Blobs { get; set; } = null!;
    internal IResourceBuilder<QdrantServerResource> VectorDb { get; set; } = null!;

    // Deferred infrastructure customization callbacks (applied after AddIAW returns)
    internal Action<IResourceBuilder<AzureStorageResource>>? StorageCallback { get; set; }
    internal Action<IResourceBuilder<QdrantServerResource>>? VectorDbCallback { get; set; }

    // API key parameters — GitHub token is always created (needed for Octokit even without GitHub LLM)
    internal IResourceBuilder<ParameterResource>? AnthropicKeyParam { get; set; }
    internal IResourceBuilder<ParameterResource>? OpenAiKeyParam { get; set; }
    internal IResourceBuilder<ParameterResource> GitHubTokenParam { get; set; } = null!;

    // Client-side reference for non-silo apps
    public IResourceBuilder<OrleansService> AsClient() => Orleans.AsClient();
}
```

### Public API

```csharp
public static class IAWHostingExtensions
{
    // Entry point — creates Orleans service + default infrastructure
    public static IAWService AddIAW(
        this IDistributedApplicationBuilder builder,
        string name = "iaw");

    // LLM model registration
    public static IAWService WithLLM<TModel>(this IAWService iaw)
        where TModel : LLMModel;

    // Ollama with user configuration
    public static IAWService WithOllama(
        this IAWService iaw,
        Action<IResourceBuilder<OllamaResource>> configure);

    // Voice-to-text
    public static IAWService WithVoice2Text(this IAWService iaw);
    public static IAWService WithVoice2Text<TModel>(this IAWService iaw)
        where TModel : WhisperModel;

    // Infrastructure overrides (optional — defaults are set by AddIAW)
    public static IAWService WithStorage(
        this IAWService iaw,
        Action<IResourceBuilder<AzureStorageResource>> configure);

    public static IAWService WithVectorDb(
        this IAWService iaw,
        Action<IResourceBuilder<QdrantServerResource>> configure);

    // Production storage override
    public static IAWService WithCosmosStorage(
        this IAWService iaw,
        IResourceBuilder<AzureCosmosDBResource> cosmos);

    // WithReference overload for silo projects
    public static IResourceBuilder<T> WithReference<T>(
        this IResourceBuilder<T> builder,
        IAWService iaw)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport;
}
```

### WithReference Integration

`WithReference(iaw)` propagates everything a project needs — Orleans, infrastructure, API keys, models. No additional calls required.

```csharp
public static IResourceBuilder<T> WithReference<T>(
    this IResourceBuilder<T> builder,
    IAWService iaw)
    where T : IResourceWithEnvironment, IResourceWithWaitSupport
{
    // Orleans cluster membership
    builder.WithReference(iaw.Orleans);

    // Infrastructure connections + wait-for (services must be ready before silo starts)
    builder.WithReference(iaw.Blobs).WaitFor(iaw.Blobs);
    builder.WithReference(iaw.VectorDb).WaitFor(iaw.VectorDb);

    // LLM model declarations
    for (var i = 0; i < iaw.DeclaredModels.Count; i++)
    {
        var model = iaw.DeclaredModels[i];
        var prefix = $"AI__LLM__Models__{i}";
        builder.WithEnvironment($"{prefix}__Id", model.Id);
        builder.WithEnvironment($"{prefix}__Provider", model.Provider);
        builder.WithEnvironment($"{prefix}__ServiceKey", model.ServiceKey);
    }

    // API keys (only if the provider was declared)
    if (iaw.AnthropicKeyParam is not null)
        builder.WithEnvironment("AI__LLM__AnthropicApiKey", iaw.AnthropicKeyParam);
    if (iaw.OpenAiKeyParam is not null)
        builder.WithEnvironment("AI__LLM__OpenAiApiKey", iaw.OpenAiKeyParam);

    // GitHub token always propagated (needed for Octokit regardless of LLM provider)
    builder.WithEnvironment("AI__LLM__GitHubToken", iaw.GitHubTokenParam);
    builder.WithEnvironment("GitHub__Token", iaw.GitHubTokenParam);

    // Ollama model resources
    foreach (var modelResource in iaw.OllamaModelResources)
        builder.WithReference(modelResource);

    // Whisper
    if (iaw.WhisperModel is not null)
        builder.WithEnvironment("AI__Whisper__ModelId", iaw.WhisperModel.Id);

    return builder;
}
```

### AddIAW Default Behavior

`AddIAW` creates infrastructure with sensible defaults. `WithStorage` and `WithVectorDb` store callbacks that are applied to the raw resource builders — the callbacks REPLACE the default configuration rather than layering on top. This avoids double-`RunAsEmulator` issues.

```csharp
public static IAWService AddIAW(
    this IDistributedApplicationBuilder builder,
    string name = "iaw")
{
    var orleans = builder.AddOrleans(name)
        .WithClusterId("dev")
        .WithServiceId("dev")
        .WithDevelopmentClustering()
        .WithMemoryGrainStorage("Default")
        .WithMemoryGrainStorage("PubSubStore")
        .WithMemoryStreaming(IAWConstants.StreamProvider)
        .WithMemoryReminders();

    var iaw = new IAWService(orleans, builder);

    // GitHub token is always created (Octokit needs it regardless of LLM provider choice)
    iaw.GitHubTokenParam = builder.AddParameter("github-token", secret: true);

    // Default infrastructure — created raw, then configured
    var storage = builder.AddAzureStorage("iaw-storage");
    iaw.Blobs = storage.AddBlobs("file-storage");
    iaw.VectorDb = builder.AddQdrant("qdrant");
    iaw.Storage = storage;

    // Apply default or user-provided configuration
    // Storage: default is emulator with data volume
    // VectorDb: default is data volume
    // If WithStorage/WithVectorDb was chained, those callbacks replace defaults
    builder.Configuration.GetSection("IAW"); // trigger config binding

    // Deferred: ApplyInfrastructureDefaults is called when the first WithReference resolves
    // This allows WithStorage/WithVectorDb to be chained after AddIAW before defaults apply
    return iaw;
}

// Called internally before first WithReference resolves infrastructure
internal static void ApplyInfrastructureDefaults(IAWService iaw)
{
    if (iaw.StorageCallback is not null)
        iaw.StorageCallback(iaw.Storage);
    else
        iaw.Storage.RunAsEmulator(e => e.WithDataVolume("iaw-blobs"));

    if (iaw.VectorDbCallback is not null)
        iaw.VectorDbCallback(iaw.VectorDb);
    else
        iaw.VectorDb.WithDataVolume();
}
```

### User-Configurable Infrastructure

All infrastructure created by `AddIAW()` has sensible defaults. Users override via lambda callbacks that REPLACE (not layer on) the defaults:

```csharp
// Default: Azure Storage emulator with data volume, Qdrant with data volume
var iaw = builder.AddIAW("iaw")
    .WithLLM<Sonnet46>();

// Custom storage (callback replaces default RunAsEmulator config)
var iaw = builder.AddIAW("iaw")
    .WithStorage(s => s.RunAsEmulator(e => e
        .WithDataVolume("my-custom-volume")
        .WithLifetime(ContainerLifetime.Persistent)))
    .WithVectorDb(q => q
        .WithDataVolume("my-qdrant-data")
        .WithLifetime(ContainerLifetime.Persistent))
    .WithLLM<Sonnet46>();

// Production: Cosmos instead of memory grain storage
var cosmos = builder.AddAzureCosmosDB("cosmos");
var iaw = builder.AddIAW("iaw")
    .WithCosmosStorage(cosmos)
    .WithLLM<Sonnet46>();
```

### Resulting AppHost.cs

```csharp
using Aspire.Hosting;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithVoice2Text<WhisperLargeV3Turbo>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());

var assistant = builder.AddProject<Projects.IAW_Assistant>("assistant")
    .WithReference(iaw)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30000; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11111; })
    .WithUrlForEndpoint("https", ep => new()
    {
        Url = "/dashboard",
        DisplayText = "Orleans Dashboard"
    });

builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WaitFor(assistant);

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithHttpEndpoint(port: 5300, name: "mcp-direct", isProxied: false)
    .WaitFor(assistant);

var ngrokAuthToken = builder.AddParameter("ngrok-auth-token", secret: true);
var ngrok = builder.AddNgrok("ngrok").WithAuthToken(ngrokAuthToken);

var botToken = builder.AddParameter("bot-token", secret: true);
var telegram = builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Telegram__BotToken", botToken)
    .WithEnvironment("Telegram__NgrokApiUrl", ngrok.GetEndpoint("http"))
    .WaitFor(assistant);

ngrok.WithTunnelEndpoint(telegram, "http");

builder.AddViteApp("website", "../../website")
    .WithNpm()
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

---

## Package: Aspire.IAW.Client

### Project

- **Path:** `src/Aspire.IAW.Client/Aspire.IAW.Client.csproj`
- **PackageId:** `Aspire.IAW.Client`
- **RootNamespace:** `Aspire.IAW`
- **IsPackable:** true
- **Tags:** `aspire`, `client`, `integration`, `orleans`, `agents`, `ai`, `iaw`
- **Description:** `Aspire client integration for the IAW multi-agent runtime`

### Dependencies

- `IAW.Core`
- `Microsoft.Orleans.Server` (silo-side — see note below)
- `Microsoft.Orleans.Dashboard`
- `Microsoft.Orleans.Journaling`
- `Microsoft.Orleans.Persistence.Memory`
- `Microsoft.Orleans.Reminders`
- `Microsoft.Orleans.Streaming`
- `Aspire.Azure.Storage.Blobs`
- `Aspire.Qdrant.Client`
- `OpenTelemetry.*` packages (exporter, hosting, ASP.NET Core, HTTP, runtime instrumentation)
- `Microsoft.Extensions.Http.Resilience`
- `Microsoft.Extensions.ServiceDiscovery`

> **Note on Orleans.Server:** This package provides both `AddIAW()` (silo) and `AddIAWClient()` (client). `Microsoft.Orleans.Server` is only needed by silo consumers. For pre-1.0, a single package is acceptable. Post-1.0, consider splitting into `Aspire.IAW.Server` and `Aspire.IAW.Client` if the transitive dependency size matters.

### Public API

```csharp
public static class IAWClientExtensions
{
    // For silo apps (e.g., IAW.Assistant)
    // Does everything: Orleans silo + LLM providers + embedding + attribute mappers +
    //   blob client + qdrant client + BlobFileStorage + OTel + health + service discovery
    public static TBuilder AddIAW<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder;

    // For non-silo apps (e.g., MCP, DevUI, Telegram)
    // Orleans client + OTel + health + service discovery
    public static TBuilder AddIAWClient<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder;

    // Health check endpoints
    public static WebApplication MapDefaultEndpoints(this WebApplication app);
}
```

### AddIAW (Silo) Implementation

Merges everything from ServiceDefaults + LlmRegistration into one call:

```csharp
public static TBuilder AddIAW<TBuilder>(this TBuilder builder)
    where TBuilder : IHostApplicationBuilder
{
    // 1. OTel, health checks, service discovery, resilience
    builder.ConfigureOpenTelemetry();
    builder.AddDefaultHealthChecks();
    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });

    // 2. Orleans silo (Aspire injects clustering config via env vars from WithReference)
    builder.UseOrleans(silo =>
    {
        silo.Configure<Orleans.Configuration.EndpointOptions>(ep =>
            ep.AdvertisedIPAddress = System.Net.IPAddress.Loopback);
        silo.Services.AddSingleton<IStateMachineStorageProvider,
            VolatileStateMachineStorageProvider>();
        silo.AddStateMachineStorage();
        silo.AddDashboard();
    });

    // 3. LLM providers (reads AI__LLM__Models__* from env, registers IChatClient instances)
    builder.AddLlmProviders();

    // 4. Embedding provider
    builder.AddEmbeddingProvider();

    // 5. Aspire client connections (names match what Aspire.Hosting.IAW creates)
    builder.AddAzureBlobServiceClient("file-storage");
    builder.AddQdrantClient("qdrant");
    builder.Services.AddSingleton<BlobFileStorage>();

    return builder;
}
```

### AddIAWClient Implementation

```csharp
public static TBuilder AddIAWClient<TBuilder>(this TBuilder builder)
    where TBuilder : IHostApplicationBuilder
{
    // 1. OTel, health checks, service discovery, resilience
    builder.ConfigureOpenTelemetry();
    builder.AddDefaultHealthChecks();
    builder.Services.AddServiceDiscovery();
    builder.Services.ConfigureHttpClientDefaults(http =>
    {
        http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
    });

    // 2. Orleans client (Aspire injects clustering config via env vars from WithReference)
    builder.UseOrleansClient();

    return builder;
}
```

### LlmRegistration

`AddLlmProviders()` and `AddEmbeddingProvider()` become **internal** methods in the client package. They are called by `AddIAW()` — never by user code.

The **attribute mapper registration helper** (`RegisterAttributeMapper`) remains as a `public static` method in `IAW.Core` (in a new `Core/AI/LlmAttributeMapperRegistration.cs`) so that `IAW.Testing/AgentTest.cs` can continue using it without taking a dependency on the full `Aspire.IAW.Client` package. Only the provider factory registration and DI extension methods move to the client package.

Split:
- **Stays in `IAW.Core`:** `LlmAttributeMapperRegistration.RegisterAttributeMapper()`, `RegisterAllAttributeMappers()`, all types (`LLMModel`, `LlmAttribute<T>`, `ILlmProviderFactory`, `LlmConfig`, `BlobFileStorage`)
- **Moves to `Aspire.IAW.Client`:** `AddLlmProviders()`, `AddEmbeddingProvider()`, provider factory implementations (`AnthropicProviderFactory`, `OpenAIProviderFactory`, `OllamaProviderFactory`, `GitHubProviderFactory`)

### Resulting Program.cs Files

```csharp
// IAW.Assistant (silo) — entire file
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);
builder.AddIAW();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapGet("/", () => "IAW Assistant Silo");
app.Run();
```

```csharp
// MCP — entire file
var builder = WebApplication.CreateBuilder(args);
builder.AddIAWClient();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithTools<AgentTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp();
app.Run();
```

```csharp
// DevUI
var builder = WebApplication.CreateBuilder(args);
builder.AddIAWClient();
builder.Services.AddSingleton<IChatClient, OrleansAgentChatClient>();
AgentDiscovery.DiscoverAndRegisterAgents(builder);
builder.Services.AddOpenAIResponses();
builder.Services.AddOpenAIConversations();

var app = builder.Build();
app.UseHttpsRedirection();
app.MapDefaultEndpoints();
app.MapOpenAIResponses();
app.MapOpenAIConversations();
if (builder.Environment.IsDevelopment()) app.MapDevUI();
app.Run();
```

```csharp
// Telegram
var builder = WebApplication.CreateBuilder(args);
builder.AddIAWClient();
builder.AddAzureBlobServiceClient("file-storage");  // Telegram needs blob access directly
builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));
// ... Telegram-specific services
```

---

## Deleted

| What | Why |
|------|-----|
| `src/IAW.ServiceDefaults/` | Entire project — merged into `Aspire.IAW.Client` |
| `src/Core/AI/LlmRegistration.cs` | Split: mappers stay in Core, providers move to `Aspire.IAW.Client` |
| `src/IAW.AppHost/IAWExtensions.cs` | Moves to `Aspire.Hosting.IAW` package |
| `WithLLMEnvironment()` | Replaced by `WithReference(iaw)` auto-propagation |

## Kept in IAW.Core

| What | Why |
|------|-----|
| `LLMModel`, model types | Type definitions used by both packages |
| `WhisperModel` | Type definition |
| `LlmAttribute<T>`, `AgentStateAttribute`, etc. | Orleans attribute types |
| `ILlmProviderFactory` | Interface contract |
| `LlmConfig` | Configuration key constants |
| `LlmAttributeMapperRegistration` | Shared by client package and `IAW.Testing` |
| `BlobFileStorage` | Service type (registered by client package) |
| `IAWConstants` | Shared constants |
| Agent base class, contracts, tools, etc. | Core library |

---

## IAW.Testing Impact

`AgentTest<TAgent>` currently registers attribute mappers and mock LLM clients directly. After this refactor:

- `AgentTest` continues to reference `IAW.Core` only (no Aspire.IAW.Client dependency)
- Attribute mapper registration uses `LlmAttributeMapperRegistration.RegisterAllAttributeMappers(siloBuilder)` from Core
- Mock `IChatClient` and `IEmbeddingGenerator` registration stays in `AgentTest` (unchanged)
- No Aspire client packages (blob, qdrant) are needed in tests — tests use memory storage

---

## Project References (after)

```
Aspire.Hosting.IAW
  -> IAW.Core
  -> Aspire.Hosting.Orleans
  -> Aspire.Hosting.Azure.Storage
  -> Aspire.Hosting.Qdrant
  -> CommunityToolkit.Aspire.Hosting.Ollama

Aspire.IAW.Client
  -> IAW.Core
  -> Microsoft.Orleans.Server
  -> Microsoft.Orleans.Dashboard
  -> Microsoft.Orleans.Journaling
  -> Aspire.Azure.Storage.Blobs
  -> Aspire.Qdrant.Client
  -> OpenTelemetry.*
  -> Microsoft.Extensions.Http.Resilience
  -> Microsoft.Extensions.ServiceDiscovery

IAW.AppHost
  -> Aspire.Hosting.IAW (replaces inline IAWExtensions.cs)
  -> project references for AddProject<> (IAW.Assistant, DevUI, MCP, Telegram)

IAW.Assistant
  -> IAW.Core, IAW.Agents, IAW.Agents.CSharp
  -> Aspire.IAW.Client (replaces ServiceDefaults)

MCP, DevUI
  -> Aspire.IAW.Client (replaces ServiceDefaults)

Telegram
  -> IAW.Core
  -> Aspire.IAW.Client (replaces ServiceDefaults)

IAW.Testing
  -> IAW.Core only (unchanged)
```

---

## Migration Checklist

1. Extract `LlmAttributeMapperRegistration` from `LlmRegistration.cs` into `Core/AI/LlmAttributeMapperRegistration.cs`
2. Create `src/Aspire.Hosting.IAW/` project and csproj with proper package metadata and tags
3. Move `IAWExtensions.cs` content into `IAWHostingExtensions.cs`, refactor to `IAWService` instance state (eliminate all static fields)
4. Create `src/Aspire.IAW.Client/` project and csproj with proper package metadata and tags
5. Move `ServiceDefaults/Extensions.cs` content (OTel, health, service discovery) into client package
6. Move `LlmRegistration.cs` provider registration into client package (keep mapper helpers in Core)
7. Update `IAW.AppHost` to reference `Aspire.Hosting.IAW`, rewrite `AppHost.cs` to use `IAWService` fluent API
8. Update `IAW.Assistant` to reference `Aspire.IAW.Client`, simplify `Program.cs` to `builder.AddIAW()`
9. Update `MCP`, `DevUI`, `Telegram` to reference `Aspire.IAW.Client`, simplify to `builder.AddIAWClient()`
10. Update `IAW.Testing/AgentTest.cs` to use `LlmAttributeMapperRegistration` from Core
11. Delete `src/IAW.ServiceDefaults/`
12. Delete `WithLLMEnvironment` — replaced by `WithReference(iaw)` auto-propagation
13. Update `IAW.slnx` — add new projects, remove ServiceDefaults
14. Update `Directory.Packages.props` if new package versions are needed
15. Update `nuget.yml` CI workflow to pack `Aspire.Hosting.IAW` and `Aspire.IAW.Client`
16. Update `CLAUDE.md` project layout table
17. Build and test
