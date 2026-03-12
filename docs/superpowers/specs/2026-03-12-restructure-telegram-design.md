# IAW Restructuring: Assistant Silo, Telegram Client, Dead Code Cleanup

## Goal

Separate the production assistant from demo samples, get Telegram working as an Orleans client with two-way agent communication and cross-channel memory, and clean up dead code from the V2-to-V3 transition.

## Architecture Overview

```
IAW.AppHost (Aspire orchestration)
  |
  +-- IAW.Assistant (Orleans SILO - production)
  |     All agents: PersonalAssistant, Memory, LLM, Orchestration, Infrastructure
  |     Orleans Dashboard at /dashboard
  |     Ports: 11111 (silo), 30000 (gateway)
  |
  +-- Samples (Orleans SILO - demo only)
  |     HTTP demo endpoints at /samples/*
  |     Own silo, own ports (11113/30002)
  |     No clients depend on it
  |
  +-- DevUI (Orleans CLIENT -> Assistant gateway)
  +-- MCP (Orleans CLIENT -> Assistant gateway)
  +-- Telegram (Orleans CLIENT -> Assistant gateway)
  |     Webhook endpoint at /webhook
  |     Subscribes to agent notification streams
  |     Forum topics: Assistant, Notifications
  |
  +-- Ollama (container)
```

## Workstream 1: Create IAW.Assistant Production Silo

### What

New project `src/IAW.Assistant/` — minimal Orleans silo that hosts all agents.

### Files

- Create: `src/IAW.Assistant/IAW.Assistant.csproj`
- Create: `src/IAW.Assistant/Program.cs`

### IAW.Assistant.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <NoWarn>$(NoWarn);NU1603</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Dashboard" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
    <PackageReference Include="Microsoft.Orleans.Persistence.Memory" />
    <PackageReference Include="Microsoft.Orleans.Reminders" />
    <PackageReference Include="Microsoft.Orleans.Server" />
    <PackageReference Include="Microsoft.Orleans.Streaming" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
    <ProjectReference Include="..\Agents\Agents.csproj" />
    <ProjectReference Include="..\Agents.CSharp\Agents.CSharp.csproj" />
    <ProjectReference Include="..\IAW.ServiceDefaults\ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

### Program.cs

Minimal — same pattern as current `samples/Samples/Program.cs` but without the HTTP demo endpoints:

```csharp
using Core.AI;
using Orleans.Dashboard;
using Orleans.Journaling;
using ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

var siloPort = builder.Configuration.GetValue("Orleans:Endpoints:SiloPort", 11_111);
var gatewayPort = builder.Configuration.GetValue("Orleans:Endpoints:GatewayPort", 30_000);
var clusterId = builder.Configuration.GetValue("Orleans:ClusterId", "dev");
var serviceId = builder.Configuration.GetValue("Orleans:ServiceId", "dev");

builder.Host.UseOrleans(silo =>
{
    silo.UseLocalhostClustering(
        siloPort: siloPort,
        gatewayPort: gatewayPort,
        serviceId: serviceId,
        clusterId: clusterId);
    silo.Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>();
    silo.AddStateMachineStorage();
    silo.AddDashboard();
});

builder.AddLlmProviders();
builder.AddServiceDefaults();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapOrleansDashboard(routePrefix: "/dashboard");
app.MapGet("/", () => "IAW Assistant Silo");
app.Run();
```

### Samples changes

`samples/Samples/` stays as-is. Its ports change to 11113/30002 in AppHost to avoid collision. It runs independently — no other services depend on it.

## Workstream 2: AppHost Rewiring

### What

Update `src/IAW.AppHost/AppHost.cs` to add `IAW.Assistant` as primary silo and rewire all clients.

### Changes

```csharp
var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());

// Production silo
var assistant = builder.AddProject<Projects.IAW_Assistant>("assistant")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30000; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11111; })
    .WithUrlForEndpoint("https", ep => new()
    {
        Url = "/dashboard",
        DisplayText = "Orleans Dashboard"
    });

// Demo silo (independent)
builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30002; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11113; });

// Clients all point to assistant silo
builder.AddProject<Projects.DevUI>("devui")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder)
    .WithEnvironment("Orleans__PrimaryGateway", assistant.GetEndpoint("orleans-gateway"))
    .WaitFor(assistant);

builder.AddProject<Projects.MCP>("mcp")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Orleans__PrimaryGateway", assistant.GetEndpoint("orleans-gateway"))
    .WithHttpEndpoint(port: 5300, name: "mcp-direct", isProxied: false)
    .WaitFor(assistant);

// Telegram client
var botToken = builder.AddParameter("bot-token", secret: true);
var telegram = builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(iaw.AsClient())
    .WithLLMEnvironment(builder)
    .WithEnvironment("Orleans__PrimaryGateway", assistant.GetEndpoint("orleans-gateway"))
    .WithEnvironment("Telegram__BotToken", botToken)
    .WaitFor(assistant);
```

Note: ngrok removed for now. Webhook URL can be set via config or environment variable directly. Qdrant is also deferred — not needed until vector search is wired for real embeddings.

## Workstream 3: Telegram Client Rewrite

### What

Rewrite `src/Clients.Telegram.Bot/` as an Orleans client (not silo). Rename to `src/Clients.Telegram/`.

### Delete

- `TelegramConversation.cs` — V2 grain, replaced by direct PA calls
- `ITelegramConversation.cs` — V2 interface and all its models
- `AgentRouter.cs` — PA has built-in delegation via `AssignTaskToAgent`
- `MonitorSourceProvider.cs` — unused RSS monitoring

### Keep (with modifications)

- `Program.cs` — rewrite as Orleans client
- `WebhookSetupService.cs` — keep webhook registration
- `Services/VoiceTranscriptionService.cs` — keep voice support
- `Services/AudioConverter.cs` — keep audio conversion
- `Services/VoiceCallService.cs` — keep call support

### New files

- `TelegramBotService.cs` — handles webhook updates, manages forum topics, sends messages
- `StreamSubscriber.cs` — subscribes to Orleans notification stream, routes to Telegram topics
- `TelegramBotOptions.cs` — config record (already exists partially)

### Program.cs (rewritten)

```csharp
using System.Net;
using Microsoft.Extensions.Options;
using ServiceDefaults;
using Telegram.BotAPI;
using TelegramClient;
using TelegramClient.Services;
using BotUpdate = Telegram.BotAPI.GettingUpdates.Update;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Orleans CLIENT (not silo)
var gatewayAddress = builder.Configuration["Orleans:PrimaryGateway"];
var clusterId = builder.Configuration.GetValue("Orleans:ClusterId", "dev");
var serviceId = builder.Configuration.GetValue("Orleans:ServiceId", "dev");

builder.UseOrleansClient(client =>
{
    client.Configure<Orleans.Configuration.ClusterOptions>(options =>
    {
        options.ClusterId = clusterId;
        options.ServiceId = serviceId;
    });

    if (!string.IsNullOrEmpty(gatewayAddress))
    {
        var uri = new Uri(gatewayAddress);
        client.UseStaticClustering(new IPEndPoint(IPAddress.Loopback, uri.Port));
    }
    else
    {
        client.UseLocalhostClustering();
    }

    client.AddMemoryStreams("agents");
});

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));
builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IOptions<TelegramBotOptions>>().Value;
    return new TelegramBotClient(config.BotToken);
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<TelegramBotService>();
builder.Services.AddHostedService<StreamSubscriber>();
builder.Services.AddHostedService<WebhookSetupService>();
builder.Services.AddSingleton<IAudioConverter, AudioConverter>();
builder.Services.AddSingleton<IVoiceTranscriptionService, VoiceTranscriptionService>();

builder.AddLlmProviders();

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapPost("/webhook", async (
    HttpContext context,
    TelegramBotService botService,
    IOptions<TelegramBotOptions> options,
    CancellationToken ct) =>
{
    var secret = options.Value.WebhookSecretToken;
    if (!string.IsNullOrWhiteSpace(secret))
    {
        var headerValue = context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (!string.Equals(headerValue, secret, StringComparison.Ordinal))
            return Results.Unauthorized();
    }

    var update = await context.Request.ReadFromJsonAsync<BotUpdate>(ct);
    if (update is null)
        return Results.BadRequest();

    await botService.HandleUpdateAsync(update, ct);
    return Results.Ok();
});

app.MapGet("/", () => "IAW Telegram Client");
app.Run();
```

### TelegramBotService.cs

Handles all Telegram interaction. Key responsibilities:
- Extract text/voice from updates
- Route to PersonalAssistant via Orleans client
- Stream responses back as Telegram message edits (typing indicator + progressive edits)
- Manage forum topics (ensure Assistant and Notifications topics exist)
- Handle inline keyboard callbacks

```csharp
public class TelegramBotService(
    IClusterClient clusterClient,
    ITelegramBotClient botClient,
    IVoiceTranscriptionService voiceService,
    IOptions<TelegramBotOptions> options)
{
    private int? _assistantTopicId;
    private int? _notificationsTopicId;

    public async Task HandleUpdateAsync(BotUpdate update, CancellationToken ct)
    {
        var chatId = update.Message?.Chat.Id
            ?? update.CallbackQuery?.Message?.Chat.Id ?? 0L;
        if (chatId == 0) return;

        var text = update.Message?.Text;
        var voiceFileId = update.Message?.Voice?.FileId;

        // Voice transcription
        if (!string.IsNullOrEmpty(voiceFileId) && string.IsNullOrEmpty(text))
            text = await voiceService.TranscribeAsync(voiceFileId, ct);

        if (string.IsNullOrEmpty(text)) return;

        // Route to PersonalAssistant
        var pa = clusterClient.GetGrain<IPersonalAssistant>("personal-assistant");

        // Send typing indicator
        await botClient.SendChatActionAsync(chatId, "typing", ...);

        // Stream response with progressive message edits
        var threadId = update.Message?.MessageThreadId ?? _assistantTopicId;
        var sentMessage = await botClient.SendMessageAsync(chatId, "...", threadId, ...);

        var buffer = new StringBuilder();
        var lastEdit = DateTimeOffset.MinValue;

        await foreach (var chunk in pa.GetResponseStream(text, ct))
        {
            buffer.Append(chunk);
            if ((DateTimeOffset.UtcNow - lastEdit).TotalMilliseconds > 500)
            {
                await botClient.EditMessageTextAsync(chatId, sentMessage.MessageId, buffer.ToString(), ...);
                lastEdit = DateTimeOffset.UtcNow;
            }
        }

        // Final edit with complete text
        await botClient.EditMessageTextAsync(chatId, sentMessage.MessageId, buffer.ToString(), ...);
    }
}
```

### StreamSubscriber.cs

Subscribes to Orleans agent notification stream to receive proactive agent messages:

```csharp
public class StreamSubscriber(
    IClusterClient clusterClient,
    TelegramBotService botService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var streamProvider = clusterClient.GetStreamProvider("agents");
        var stream = streamProvider.GetStream<AgentEvent>("agent-notifications", "notifications");

        await stream.SubscribeAsync((evt, token) =>
        {
            // Route event to appropriate Telegram forum topic
            return botService.SendNotificationAsync(evt, ct);
        });
    }
}
```

### Memory

No Telegram-specific memory work needed. PersonalAssistant already:
- Stores facts via `RememberFact` tool -> UserMemory grain
- Retrieves memories via `MemoryContextProvider` on every prompt
- Works identically from all channels (DevUI, MCP, Telegram)

User says "my birthday is March 15" via Telegram -> PA stores it -> next conversation from any channel -> PA knows.

## Workstream 4: Dead Code Cleanup

### Files to delete

| File | Reason |
|------|--------|
| `src/Clients.Telegram.Bot/TelegramConversation.cs` | V2 grain replaced by PA |
| `src/Clients.Telegram.Bot/ITelegramConversation.cs` | V2 interface and models |
| `src/Clients.Telegram.Bot/AgentRouter.cs` | PA handles routing |
| `src/Clients.Telegram.Bot/MonitorSourceProvider.cs` | Unused RSS feature |

### Commented-out code to clean

| Location | What |
|----------|------|
| `AppHost.cs` lines 30-49 | Old Telegram silo config — replaced by new client config |
| `AppHost.cs` lines 50-53 | Website ViteApp — keep TODO but clean up stale comments |
| `IAW.slnx` | Commented-out TelegramBot project reference |

### Stale TODOs to resolve

| Location | TODO | Action |
|----------|------|--------|
| `AppHost.cs:30` | "Re-enable after TelegramBot is migrated to V3 API" | Delete — replaced by new Telegram client wiring |
| `AppHost.cs:50` | "Re-enable when website directory exists" | Keep — website is a future task |
| `IAW.slnx` | "Re-enable after TelegramBot V3 migration" | Delete — add new Telegram client project |

### Telegram project rename

`src/Clients.Telegram.Bot/` -> `src/Clients.Telegram/`
- Update namespace from `TelegramBot` to `TelegramClient`
- Update csproj name from `TelegramBot.csproj` to `Telegram.csproj`
- Update all references in IAW.slnx and AppHost

### Solution file update

Add new projects, remove old references:
```xml
<Project Path="src/IAW.Assistant/IAW.Assistant.csproj" />
<Project Path="src/Clients.Telegram/Telegram.csproj" />
```

## Testing Strategy

### Unit tests

- No new unit tests needed for IAW.Assistant (it's just hosting)
- Existing agent tests remain valid — agents don't change
- TelegramBotService can be tested with mock IClusterClient and mock ITelegramBotClient

### Integration tests

- Verify IAW.Assistant silo starts and accepts client connections
- Verify PersonalAssistant is reachable from the gateway
- Verify Samples silo runs independently without breaking production

### Manual verification

- `aspire run` — all resources start green
- DevUI chat with PersonalAssistant works
- MCP tools respond
- Telegram webhook receives and responds (requires bot token + webhook URL)

## Build Order

1. Create `IAW.Assistant` project (no dependencies change)
2. Update AppHost to add `assistant`, rewire clients
3. Change Samples ports to 11113/30002
4. Test: `aspire run` — verify assistant silo + clients work, samples independent
5. Rename and rewrite Telegram project
6. Delete dead code (V2 files, stale TODOs, commented blocks)
7. Update IAW.slnx
8. Final: `dotnet build IAW.slnx && dotnet test IAW.slnx && aspire run`
