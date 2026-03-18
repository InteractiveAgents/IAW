# Aspire Integration Packages Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract Aspire hosting and client integrations into `Aspire.Hosting.IAW` and `Aspire.IAW.Client` packages, eliminating `IAW.ServiceDefaults` and making silo startup a single `builder.AddIAW()` call.

**Architecture:** Two new packages following the `Aspire.Hosting.Qdrant` / `Aspire.Qdrant.Client` convention. `IAWService` replaces static mutable state with instance-scoped configuration. `WithReference(iaw)` auto-propagates all environment variables, API keys, and infrastructure connections.

**Tech Stack:** .NET 11, Orleans 10, Aspire 13.1.2, OpenTelemetry 1.15

**Spec:** `docs/superpowers/specs/2026-03-16-aspire-integration-packages-design.md`

---

## File Map

### New Files

| File | Responsibility |
|------|---------------|
| `src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj` | Hosting package project definition |
| `src/Aspire.Hosting.IAW/IAWService.cs` | Central config object replacing static state |
| `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs` | `AddIAW()`, `WithLLM<T>()`, `WithReference(iaw)`, etc. |
| `src/Aspire.IAW.Client/Aspire.IAW.Client.csproj` | Client package project definition |
| `src/Aspire.IAW.Client/IAWClientExtensions.cs` | `AddIAW()` (silo), `AddIAWClient()`, `MapDefaultEndpoints()` |
| `src/Aspire.IAW.Client/LlmRegistration.cs` | Provider factories + `AddLlmProviders()`/`AddEmbeddingProvider()` (internal) |
| `src/Aspire.IAW.Client/OpenTelemetryExtensions.cs` | OTel configuration (from ServiceDefaults) |
| `src/Core/AI/LlmAttributeMapperRegistration.cs` | Extracted mapper helpers shared by client + testing |

### Modified Files

| File | Change |
|------|--------|
| `src/IAW.AppHost/Aspire.csproj` | Replace inline code deps with `Aspire.Hosting.IAW` project ref |
| `src/IAW.AppHost/AppHost.cs` | Rewrite to use `IAWService` fluent API |
| `src/IAW.Assistant/IAW.Assistant.csproj` | Replace ServiceDefaults ref with `Aspire.IAW.Client` ref, remove Orleans/Aspire packages |
| `src/IAW.Assistant/Program.cs` | Simplify to `builder.AddIAW()` |
| `src/IAW.MCP/MCP.csproj` | Replace ServiceDefaults ref with `Aspire.IAW.Client` ref |
| `src/IAW.MCP/Program.cs` | Replace `AddServiceDefaults()+AddIAWClient()` with `AddIAWClient()` |
| `src/DevUI/DevUI.csproj` | Replace ServiceDefaults ref with `Aspire.IAW.Client` ref |
| `src/DevUI/Program.cs` | Replace `AddServiceDefaults()+AddIAWClient()` with `AddIAWClient()` |
| `src/Clients.Telegram/Telegram.csproj` | Replace ServiceDefaults ref with `Aspire.IAW.Client` ref |
| `src/Clients.Telegram/Program.cs` | Replace `AddServiceDefaults()+AddIAWClient()` with `AddIAWClient()` |
| `src/IAW.Testing/AgentTest.cs` | Use `LlmAttributeMapperRegistration` from Core |
| `IAW.slnx` | Add 2 new projects, remove ServiceDefaults |
| `Directory.Packages.props` | No changes expected (all packages already listed) |
| `.github/workflows/nuget.yml` | Add 2 new pack commands |
| `CLAUDE.md` | Update project layout table |

### Deleted Files

| File | Reason |
|------|--------|
| `src/IAW.ServiceDefaults/ServiceDefaults.csproj` | Merged into `Aspire.IAW.Client` |
| `src/IAW.ServiceDefaults/Extensions.cs` | Merged into `Aspire.IAW.Client` |
| `src/IAW.AppHost/IAWExtensions.cs` | Moved to `Aspire.Hosting.IAW` |
| `src/Core/AI/LlmRegistration.cs` | Split: mappers to Core helper, providers to client |

---

## Task 1: Extract attribute mapper helpers from LlmRegistration into Core

**Files:**
- Create: `src/Core/AI/LlmAttributeMapperRegistration.cs`
- Modify: `src/Core/AI/LlmRegistration.cs`
- Modify: `src/IAW.Testing/AgentTest.cs`

- [ ] **Step 1: Create `LlmAttributeMapperRegistration.cs` in Core**

Extract `RegisterAttributeMapper` and add `RegisterAllAttributeMappers` from `LlmRegistration.cs`:

```csharp
// src/Core/AI/LlmAttributeMapperRegistration.cs
using Core.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Core.AI;

public static class LlmAttributeMapperRegistration
{
    public static void RegisterAttributeMapper(IServiceCollection services, LLMModel model)
    {
        var modelType = model.GetType();
        var mapperType = typeof(LlmAttributeMapper<>).MakeGenericType(modelType);
        var attributeType = typeof(LlmAttribute<>).MakeGenericType(modelType);
        var interfaceType = typeof(IAttributeToFactoryMapper<>).MakeGenericType(attributeType);
        services.AddSingleton(interfaceType, mapperType);
    }

    public static void RegisterAllAttributeMappers(IServiceCollection services)
    {
        LLMModel.EnsureAllModelsLoaded();
        foreach (var model in LLMModel.All)
            RegisterAttributeMapper(services, model);

        services.AddSingleton<IAttributeToFactoryMapper<AgentStateAttribute>, AgentStateMapper>();
        services.AddSingleton<IAttributeToFactoryMapper<UserProfileStateAttribute>, UserProfileStateMapper>();
        services.AddSingleton<IAttributeToFactoryMapper<ProjectStateAttribute>, ProjectStateMapper>();
        services.AddSingleton<IAttributeToFactoryMapper<UISessionStateAttribute>, UISessionStateMapper>();
    }

    public static void RegisterAllAttributeMappers(IServiceCollection services, IChatClient mockClient)
    {
        RegisterAllAttributeMappers(services);
        foreach (var model in LLMModel.All)
            services.AddKeyedSingleton<IChatClient>(model.ServiceKey, mockClient);
    }
}
```

- [ ] **Step 2: Update `AgentTest.cs` to use the new helper**

Replace the manual mapper loop in `AgentTestSiloConfigurator.Configure()` with:
```csharp
LlmAttributeMapperRegistration.RegisterAllAttributeMappers(siloBuilder.Services, mockClient);
```
Remove all individual `RegisterLlmMapper<T>` calls and the `RegisterLlmMapper` private method.

- [ ] **Step 3: Build and test**

Run: `dotnet build IAW.slnx && dotnet test IAW.slnx --verbosity minimal`
Expected: Build succeeds, same 311/370 tests pass (59 pre-existing failures unchanged).

- [ ] **Step 4: Commit**

```bash
git add src/Core/AI/LlmAttributeMapperRegistration.cs src/IAW.Testing/AgentTest.cs
git commit -m "refactor: extract LlmAttributeMapperRegistration into Core for shared use"
```

---

## Task 2: Create Aspire.Hosting.IAW project

**Files:**
- Create: `src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj`
- Create: `src/Aspire.Hosting.IAW/IAWService.cs`
- Create: `src/Aspire.Hosting.IAW/IAWHostingExtensions.cs`

- [ ] **Step 1: Create the csproj**

```xml
<!-- src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Aspire.Hosting</RootNamespace>
    <PackageId>Aspire.Hosting.IAW</PackageId>
    <Version>0.1.0</Version>
    <IsPackable>true</IsPackable>
    <Description>Aspire hosting integration for the IAW multi-agent runtime</Description>
    <PackageTags>aspire;hosting;integration;orleans;agents;ai;iaw</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <Authors>IAW Contributors</Authors>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Orleans" />
    <PackageReference Include="CommunityToolkit.Aspire.Hosting.Ollama" />
    <PackageReference Include="Aspire.Hosting.Azure.Storage" />
    <PackageReference Include="Aspire.Hosting.Qdrant" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" IsAspireProjectResource="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `IAWService.cs`**

Write the `IAWService` class per the spec — primary constructor, instance state, `AsClient()` method, `ApplyInfrastructureDefaults()`, and `InfrastructureApplied` flag.

- [ ] **Step 3: Create `IAWHostingExtensions.cs`**

Port all logic from `src/IAW.AppHost/IAWExtensions.cs` into this file:
- `AddIAW()` — creates Orleans + storage + qdrant + github token param, returns `IAWService`
- `WithLLM<T>()` — registers model, creates provider-specific API key params on demand
- `WithOllama()` — configures Ollama resource
- `WithVoice2Text()` / `WithVoice2Text<T>()` — registers Whisper model
- `WithStorage()` — stores callback on `IAWService`
- `WithVectorDb()` — stores callback on `IAWService`
- `WithCosmosStorage()` — overrides grain storage on Orleans
- `WithReference(IAWService)` — calls `ApplyInfrastructureDefaults`, propagates Orleans + blobs + qdrant + WaitFor + env vars + API keys + Ollama refs + Whisper config

Key difference from current code: NO static fields. All state on `IAWService` instance.

- [ ] **Step 4: Build the new project in isolation**

Run: `dotnet build src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Aspire.Hosting.IAW/
git commit -m "feat: add Aspire.Hosting.IAW package with IAWService and hosting extensions"
```

---

## Task 3: Create Aspire.IAW.Client project

**Files:**
- Create: `src/Aspire.IAW.Client/Aspire.IAW.Client.csproj`
- Create: `src/Aspire.IAW.Client/IAWClientExtensions.cs`
- Create: `src/Aspire.IAW.Client/LlmRegistration.cs`
- Create: `src/Aspire.IAW.Client/OpenTelemetryExtensions.cs`

- [ ] **Step 1: Create the csproj**

```xml
<!-- src/Aspire.IAW.Client/Aspire.IAW.Client.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>Aspire.IAW</RootNamespace>
    <PackageId>Aspire.IAW.Client</PackageId>
    <Version>0.1.0</Version>
    <IsPackable>true</IsPackable>
    <Description>Aspire client integration for the IAW multi-agent runtime</Description>
    <PackageTags>aspire;client;integration;orleans;agents;ai;iaw</PackageTags>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <Authors>IAW Contributors</Authors>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.Orleans.Server" />
    <PackageReference Include="Microsoft.Orleans.Dashboard" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
    <PackageReference Include="Microsoft.Orleans.Persistence.Memory" />
    <PackageReference Include="Microsoft.Orleans.Reminders" />
    <PackageReference Include="Microsoft.Orleans.Streaming" />
    <PackageReference Include="Aspire.Azure.Storage.Blobs" />
    <PackageReference Include="Aspire.Qdrant.Client" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
    <PackageReference Include="Microsoft.Extensions.Http.Resilience" />
    <PackageReference Include="Microsoft.Extensions.ServiceDiscovery" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `OpenTelemetryExtensions.cs`**

Move OTel + health check config from `ServiceDefaults/Extensions.cs` into this file:
- `ConfigureOpenTelemetry()` — logging, metrics (IAW + Orleans + AI meters), tracing (with sampler, health endpoint filter)
- `AddDefaultHealthChecks()` — self health check with "live" tag
- `MapDefaultEndpoints()` — maps `/health` and `/alive` in dev

- [ ] **Step 3: Create `LlmRegistration.cs`**

Move provider factories and `AddLlmProviders()` / `AddEmbeddingProvider()` from `Core/AI/LlmRegistration.cs`. Methods become **internal**. Call `LlmAttributeMapperRegistration.RegisterAllAttributeMappers()` from Core for the mapper part.

Key: keep `ILlmProviderFactory` interface and `LlmConfig` in Core. Move `AnthropicProviderFactory`, `OpenAIProviderFactory`, `OllamaProviderFactory`, `GitHubProviderFactory` implementations and the `AddLlmProviders`/`AddEmbeddingProvider`/`AddWhisperProvider` extension methods here.

- [ ] **Step 4: Create `IAWClientExtensions.cs`**

Two public entry points:

```csharp
public static class IAWClientExtensions
{
    public static TBuilder AddIAW<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.UseOrleans(silo =>
        {
            silo.Configure<Orleans.Configuration.EndpointOptions>(ep =>
                ep.AdvertisedIPAddress = System.Net.IPAddress.Loopback);
            silo.Services.AddSingleton<IStateMachineStorageProvider,
                VolatileStateMachineStorageProvider>();
            silo.AddStateMachineStorage();
            silo.AddDashboard();
        });

        builder.AddLlmProviders();
        builder.AddEmbeddingProvider();
        builder.AddAzureBlobServiceClient("file-storage");
        builder.AddQdrantClient("qdrant");
        builder.Services.AddSingleton<BlobFileStorage>();

        return builder;
    }

    public static TBuilder AddIAWClient<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        builder.UseOrleansClient();

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app) { ... }
}
```

- [ ] **Step 5: Build the new project in isolation**

Run: `dotnet build src/Aspire.IAW.Client/Aspire.IAW.Client.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add src/Aspire.IAW.Client/
git commit -m "feat: add Aspire.IAW.Client package with silo and client extensions"
```

---

## Task 4: Wire up AppHost to use Aspire.Hosting.IAW

**Files:**
- Modify: `src/IAW.AppHost/Aspire.csproj`
- Modify: `src/IAW.AppHost/AppHost.cs`
- Delete: `src/IAW.AppHost/IAWExtensions.cs`

- [ ] **Step 1: Update AppHost csproj**

Replace the direct Core project reference and Aspire hosting packages with `Aspire.Hosting.IAW`:

```xml
<ItemGroup>
  <PackageReference Include="CommunityToolkit.Aspire.Hosting.Ngrok" />
  <PackageReference Include="Aspire.Hosting.Azure.AIFoundry" />
  <PackageReference Include="Aspire.Hosting.Azure.CosmosDB" />
  <PackageReference Include="Aspire.Hosting.JavaScript" />
</ItemGroup>

<ItemGroup>
  <ProjectReference Include="..\Aspire.Hosting.IAW\Aspire.Hosting.IAW.csproj"
                    IsAspireProjectResource="false" />
  <ProjectReference Include="..\DevUI\DevUI.csproj" />
  <ProjectReference Include="..\IAW.MCP\MCP.csproj" />
  <ProjectReference Include="..\IAW.Assistant\IAW.Assistant.csproj" />
  <ProjectReference Include="..\Clients.Telegram\Telegram.csproj" />
</ItemGroup>
```

Note: keep `Aspire.Hosting.Azure.AIFoundry`, `CosmosDB`, `JavaScript`, and `Ngrok` — these are AppHost-specific, not IAW-generic.

- [ ] **Step 2: Rewrite `AppHost.cs` using IAWService fluent API**

Replace entire contents with the resulting AppHost.cs from the spec (uses `builder.AddIAW("iaw").WithLLM<>()...` pattern, `WithReference(iaw)`, `iaw.AsClient()`).

- [ ] **Step 3: Delete `IAWExtensions.cs`**

Remove `src/IAW.AppHost/IAWExtensions.cs` — all logic moved to `Aspire.Hosting.IAW`.

- [ ] **Step 4: Build AppHost**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/IAW.AppHost/ && git rm src/IAW.AppHost/IAWExtensions.cs
git commit -m "refactor: AppHost uses Aspire.Hosting.IAW package with IAWService fluent API"
```

---

## Task 5: Wire up service projects to use Aspire.IAW.Client

**Files:**
- Modify: `src/IAW.Assistant/IAW.Assistant.csproj`
- Modify: `src/IAW.Assistant/Program.cs`
- Modify: `src/IAW.MCP/MCP.csproj`
- Modify: `src/IAW.MCP/Program.cs`
- Modify: `src/DevUI/DevUI.csproj`
- Modify: `src/DevUI/Program.cs`
- Modify: `src/Clients.Telegram/Telegram.csproj`
- Modify: `src/Clients.Telegram/Program.cs`

- [ ] **Step 1: Update IAW.Assistant**

Csproj: replace ServiceDefaults ref with `Aspire.IAW.Client` ref. Remove Orleans/Aspire package refs that are now transitive via client package (Orleans.Server, Dashboard, Journaling, Persistence.Memory, Reminders, Streaming, Aspire.Azure.Storage.Blobs, Aspire.Qdrant.Client).

Program.cs — entire file:
```csharp
using Aspire.IAW;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);
builder.AddIAW();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapGet("/", () => "IAW Assistant Silo");
app.Run();
```

- [ ] **Step 2: Update IAW.MCP**

Csproj: replace ServiceDefaults ref with `Aspire.IAW.Client` ref. Remove `Microsoft.Orleans.Sdk` (transitive).

Program.cs:
```csharp
using Aspire.IAW;

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

- [ ] **Step 3: Update DevUI**

Csproj: replace ServiceDefaults ref with `Aspire.IAW.Client` ref. Remove `Microsoft.Orleans.Sdk` (transitive).

Program.cs: replace `builder.AddServiceDefaults(); builder.AddIAWClient();` with single `builder.AddIAWClient();`. Add `using Aspire.IAW;`. Remove `using ServiceDefaults;`.

- [ ] **Step 4: Update Telegram**

Csproj: replace ServiceDefaults ref with `Aspire.IAW.Client` ref.

Program.cs: replace `builder.AddServiceDefaults(); builder.AddIAWClient();` with single `builder.AddIAWClient();`. Add `using Aspire.IAW;`. Remove `using ServiceDefaults;`.

- [ ] **Step 5: Build all service projects**

Run: `dotnet build IAW.slnx 2>&1 | grep "error CS"` (expect errors about ServiceDefaults still in slnx — fixed next task)

- [ ] **Step 6: Commit**

```bash
git add src/IAW.Assistant/ src/IAW.MCP/ src/DevUI/ src/Clients.Telegram/
git commit -m "refactor: all service projects use Aspire.IAW.Client, simplified Program.cs"
```

---

## Task 6: Delete ServiceDefaults, clean up Core, update solution

**Files:**
- Delete: `src/IAW.ServiceDefaults/ServiceDefaults.csproj`
- Delete: `src/IAW.ServiceDefaults/Extensions.cs`
- Delete: `src/Core/AI/LlmRegistration.cs`
- Modify: `IAW.slnx`

- [ ] **Step 1: Delete ServiceDefaults project**

```bash
rm -rf src/IAW.ServiceDefaults
```

- [ ] **Step 2: Delete `LlmRegistration.cs` from Core**

The extension methods moved to `Aspire.IAW.Client/LlmRegistration.cs`. The mapper helper was extracted to `Core/AI/LlmAttributeMapperRegistration.cs` in Task 1. Delete the original.

```bash
rm src/Core/AI/LlmRegistration.cs
```

- [ ] **Step 3: Update `IAW.slnx`**

Remove ServiceDefaults, add two new projects:

```xml
<Solution>
  <Folder Name="/Solution Items/">
    <File Path=".editorconfig" />
    <File Path="Directory.Packages.props" />
  </Folder>
  <Folder Name="/src/">
    <Project Path="src/Agents/Agents.csproj" />
    <Project Path="src/Agents.CSharp/Agents.CSharp.csproj" />
    <Project Path="src/IAW.Testing/IAW.Testing.csproj" />
    <Project Path="src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj" />
    <Project Path="src/Aspire.IAW.Client/Aspire.IAW.Client.csproj" />
  </Folder>
  <Project Path="src/Core/Core.csproj" />
  <Project Path="src/IAW.Assistant/IAW.Assistant.csproj" />
  <Project Path="src/Clients.Telegram/Telegram.csproj" />
  <Project Path="src/DevUI/DevUI.csproj" />
  <Project Path="src/IAW.AppHost/Aspire.csproj" />
  <Project Path="src/IAW.MCP/MCP.csproj" Id="f72a5392-8914-4f0c-a8b3-642c4a424a11" />
  <Folder Name="/test/">
    <Project Path="test/Core.Tests/IAW.Core.Tests.csproj" />
    <Project Path="test/Integration.Tests/IAW.Integration.Tests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 4: Build full solution**

Run: `dotnet build IAW.slnx`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Run tests**

Run: `dotnet test IAW.slnx --verbosity minimal`
Expected: 311+ pass (same as before, 59 pre-existing Orleans serialization failures).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "refactor: delete ServiceDefaults and LlmRegistration, update solution file"
```

---

## Task 7: Update CI and documentation

**Files:**
- Modify: `.github/workflows/nuget.yml`
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update `nuget.yml` to pack new packages**

Add two new pack lines:
```yaml
- run: |
    dotnet pack src/Core/Core.csproj -c Release -o ./nupkgs
    dotnet pack src/Agents/Agents.csproj -c Release -o ./nupkgs
    dotnet pack src/Agents.CSharp/Agents.CSharp.csproj -c Release -o ./nupkgs
    dotnet pack src/IAW.Testing/IAW.Testing.csproj -c Release -o ./nupkgs
    dotnet pack src/Aspire.Hosting.IAW/Aspire.Hosting.IAW.csproj -c Release -o ./nupkgs
    dotnet pack src/Aspire.IAW.Client/Aspire.IAW.Client.csproj -c Release -o ./nupkgs
```

- [ ] **Step 2: Update CLAUDE.md project layout table**

Update the project layout table to include:

| Project | Purpose | Packable |
|---------|---------|----------|
| `src/Aspire.Hosting.IAW` | AppHost integration: `AddIAW()`, `IAWService`, `WithLLM<T>()` | Yes |
| `src/Aspire.IAW.Client` | Service integration: silo `AddIAW()`, client `AddIAWClient()`, OTel | Yes |

Remove `src/IAW.ServiceDefaults` row. Update `src/IAW.AppHost` description to mention `Aspire.Hosting.IAW` dependency.

- [ ] **Step 3: Final build and test verification**

Run: `dotnet build IAW.slnx && dotnet test IAW.slnx --verbosity minimal`
Expected: Build green, tests same as baseline.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/nuget.yml CLAUDE.md
git commit -m "docs: update CI and CLAUDE.md for Aspire integration packages"
```
