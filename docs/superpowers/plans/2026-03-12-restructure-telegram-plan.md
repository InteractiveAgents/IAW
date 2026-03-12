# IAW Restructuring Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate production assistant from demo samples, get Telegram working as an Orleans client with memory and streaming, clean up V2 dead code.

**Architecture:** Two independent Orleans silos (IAW.Assistant for production, Samples for demos). Three Orleans clients (DevUI, MCP, Telegram) all connecting to IAW.Assistant's gateway. Telegram handles webhook updates, voice transcription, and subscribes to agent notification streams.

**Tech Stack:** .NET 11.0, Orleans 10.0, Aspire 13.1, Telegram.BotAPI 9.4.0, Concentus/NAudio for voice, OpenAI Whisper for transcription

**Spec:** `docs/superpowers/specs/2026-03-12-restructure-telegram-design.md`

---

## File Map

### New files
| File | Responsibility |
|------|---------------|
| `src/IAW.Assistant/IAW.Assistant.csproj` | Production silo project — hosts all agents |
| `src/IAW.Assistant/Program.cs` | Minimal Orleans silo setup with dashboard |
| `src/Clients.Telegram/Telegram.csproj` | Orleans client project for Telegram bot |
| `src/Clients.Telegram/Program.cs` | Orleans client setup + webhook endpoint |
| `src/Clients.Telegram/TelegramBotService.cs` | Handles Telegram updates, routes to PA, streams responses |
| `src/Clients.Telegram/StreamSubscriber.cs` | Subscribes to Orleans notification stream, sends to Telegram |
| `src/Clients.Telegram/TelegramBotOptions.cs` | Config record for bot token, webhook URL, chat ID |
| `src/Clients.Telegram/WebhookSetupService.cs` | Registers webhook URL with Telegram Bot API on startup |

### Modified files
| File | Changes |
|------|---------|
| `src/IAW.AppHost/Aspire.csproj` | Add IAW.Assistant and Telegram project references, remove Qdrant |
| `src/IAW.AppHost/AppHost.cs` | Add assistant silo, rewire clients, add Telegram client, change Samples ports |
| `IAW.slnx` | Add IAW.Assistant and Telegram, remove commented TelegramBot |

### Kept files (moved with namespace change)
| File | Notes |
|------|-------|
| `src/Clients.Telegram/Services/AudioConverter.cs` | Move from Telegram.Bot, change namespace to `TelegramClient.Services` |
| `src/Clients.Telegram/Services/VoiceTranscriptionService.cs` | Move from Telegram.Bot, change namespace to `TelegramClient.Services` |

### Deleted files
| File | Reason |
|------|--------|
| `src/Clients.Telegram.Bot/` (entire directory) | Replaced by `src/Clients.Telegram/` |
| `src/Core/Routing/IAgentRouter.cs` | Only used by deleted AgentRouter.cs |
| `src/Core/Contracts/IMonitorSourceProvider.cs` | Only used by deleted MonitorSourceProvider.cs (includes MonitorPollRequest, MonitorPollResult, MonitorFeedItem) |

---

## Chunk 1: IAW.Assistant Silo + AppHost Rewiring

### Task 1: Create IAW.Assistant project

**Files:**
- Create: `src/IAW.Assistant/IAW.Assistant.csproj`
- Create: `src/IAW.Assistant/Program.cs`

- [ ] **Step 1: Create the project directory**

```bash
mkdir -p src/IAW.Assistant
```

- [ ] **Step 2: Write IAW.Assistant.csproj**

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

- [ ] **Step 3: Write Program.cs**

Copy the silo setup pattern from `samples/Samples/Program.cs` but without any HTTP demo endpoints:

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

- [ ] **Step 4: Verify IAW.Assistant builds**

Run: `dotnet build src/IAW.Assistant/IAW.Assistant.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/IAW.Assistant/
git commit -m "feat: add IAW.Assistant production silo project"
```

---

### Task 2: Update AppHost to add assistant silo and rewire clients

**Files:**
- Modify: `src/IAW.AppHost/Aspire.csproj`
- Modify: `src/IAW.AppHost/AppHost.cs`

- [ ] **Step 1: Update Aspire.csproj — add project references**

Add these two project references to the `<ItemGroup>` with other project references:

```xml
<ProjectReference Include="..\IAW.Assistant\IAW.Assistant.csproj" />
```

Remove commented-out TelegramBot reference:
```xml
<!-- DELETE this line: -->
<!-- <ProjectReference Include="..\Clients.Telegram.Bot\TelegramBot.csproj" /> -->
```

Remove Qdrant package (deferred):
```xml
<!-- DELETE this line: -->
<PackageReference Include="Aspire.Hosting.Qdrant" />
```

- [ ] **Step 2: Rewrite AppHost.cs**

Replace the entire `AppHost.cs` with:

```csharp
using Aspire;
using Core.AI.Models;

var builder = DistributedApplication.CreateBuilder(args);

var iaw = builder.AddIAW("iaw")
    .WithLLM<Qwen25>()
    .WithLLM<Claude45Haiku>()
    .WithLLM<Sonnet46>()
    .WithLLM<GitHubGpt4oMini>()
    .WithOllama(o => o.WithGPUSupport().WithDataVolume().WithOpenWebUI());

// Production silo — hosts all agents, memory, LLM
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

// Demo silo — independent, no clients depend on it
builder.AddProject<Projects.Samples>("samples")
    .WithReference(iaw)
    .WithLLMEnvironment(builder)
    .WithEndpoint("orleans-gateway", e => { e.IsProxied = false; e.Port = 30002; })
    .WithEndpoint("orleans-silo", e => { e.IsProxied = false; e.Port = 11113; });

// Clients — all connect to assistant gateway
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

builder.Build().Run();
```

Key changes:
- `assistant` is the new primary silo (ports 11111/30000)
- `samples` moves to ports 11113/30002
- `devui` and `mcp` point to `assistant.GetEndpoint("orleans-gateway")` instead of `samples`
- All commented-out Telegram/ngrok/website/qdrant blocks removed
- Telegram client will be added in Task 6 after the project exists

- [ ] **Step 3: Verify AppHost builds**

Run: `dotnet build src/IAW.AppHost/Aspire.csproj`
Expected: Build succeeded

- [ ] **Step 4: Verify aspire run starts all resources**

Run: `aspire run --project src/IAW.AppHost/Aspire.csproj`
Expected: assistant, samples, devui, mcp, ollama all show as running in Aspire dashboard

Verify:
- Assistant silo responds at its HTTPS URL with "IAW Assistant Silo"
- DevUI loads and can chat with PersonalAssistant
- Samples silo responds independently with "Hello World!"
- MCP endpoint at port 5300 accepts connections

- [ ] **Step 5: Commit**

```bash
git add src/IAW.AppHost/Aspire.csproj src/IAW.AppHost/AppHost.cs
git commit -m "feat: add assistant silo to AppHost, rewire clients, move samples to port 11113"
```

---

### Task 3: Update solution file

**Files:**
- Modify: `IAW.slnx`

- [ ] **Step 1: Add IAW.Assistant to solution**

Add to the `/src/` folder section:

```xml
<Project Path="src/IAW.Assistant/IAW.Assistant.csproj" />
```

Remove the commented-out TelegramBot line:
```xml
<!-- DELETE: -->
<!-- TODO: Re-enable after TelegramBot V3 migration -->
<!-- <Project Path="src/Clients.Telegram.Bot/TelegramBot.csproj" /> -->
```

- [ ] **Step 2: Verify solution builds**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add IAW.slnx
git commit -m "feat: add IAW.Assistant to solution, remove TelegramBot comment"
```

---

## Chunk 2: Telegram Client Rewrite

### Task 4: Create Telegram client project structure

**Files:**
- Create: `src/Clients.Telegram/Telegram.csproj`
- Create: `src/Clients.Telegram/TelegramBotOptions.cs`
- Move: `src/Clients.Telegram.Bot/Services/AudioConverter.cs` → `src/Clients.Telegram/Services/AudioConverter.cs`
- Move: `src/Clients.Telegram.Bot/Services/VoiceTranscriptionService.cs` → `src/Clients.Telegram/Services/VoiceTranscriptionService.cs`

- [ ] **Step 1: Create directory structure**

```bash
mkdir -p src/Clients.Telegram/Services
```

- [ ] **Step 2: Write Telegram.csproj**

Orleans CLIENT packages only (no Server, Journaling, Persistence, Reminders). References `Agents.csproj` for `IPersonalAssistant` interface resolution:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>TelegramClient</RootNamespace>
    <NoWarn>$(NoWarn);NU1603</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Streaming" />
    <PackageReference Include="Telegram.BotAPI" />
    <PackageReference Include="Concentus" />
    <PackageReference Include="Concentus.Oggfile" />
    <PackageReference Include="NAudio" />
    <PackageReference Include="OpenAI" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
    <ProjectReference Include="..\Agents\Agents.csproj" />
    <ProjectReference Include="..\IAW.ServiceDefaults\ServiceDefaults.csproj" />
  </ItemGroup>
</Project>
```

Note: `OpenAI` package needed for `VoiceTranscriptionService` (uses `OpenAI.Audio.AudioClient`).

- [ ] **Step 3: Write TelegramBotOptions.cs**

```csharp
namespace TelegramClient;

public sealed class TelegramBotOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public long ChatId { get; set; }
}
```

- [ ] **Step 4: Copy and update AudioConverter.cs**

Copy `src/Clients.Telegram.Bot/Services/AudioConverter.cs` to `src/Clients.Telegram/Services/AudioConverter.cs`.
Change the namespace from `TelegramBot.Services` to `TelegramClient.Services`.

The implementation stays identical — only the namespace changes:

```csharp
namespace TelegramClient.Services;
// rest of file unchanged
```

- [ ] **Step 5: Copy and update VoiceTranscriptionService.cs**

Copy `src/Clients.Telegram.Bot/Services/VoiceTranscriptionService.cs` to `src/Clients.Telegram/Services/VoiceTranscriptionService.cs`.
Change the namespace from `TelegramBot.Services` to `TelegramClient.Services`.

```csharp
namespace TelegramClient.Services;
// rest of file unchanged
```

- [ ] **Step 6: Verify project builds**

Run: `dotnet build src/Clients.Telegram/Telegram.csproj`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add src/Clients.Telegram/
git commit -m "feat: create Telegram Orleans client project with audio services"
```

---

### Task 5: Write TelegramBotService

**Files:**
- Create: `src/Clients.Telegram/TelegramBotService.cs`

This is the core service — handles all Telegram webhook updates, routes to PersonalAssistant, streams responses back.

- [ ] **Step 1: Write TelegramBotService.cs**

```csharp
using System.Text;
using Core.Contracts;
using IAW.Agents.Orchestration;
using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using Telegram.BotAPI.UpdatingMessages;
using TelegramClient.Services;

namespace TelegramClient;

public sealed class TelegramBotService(
    IClusterClient clusterClient,
    ITelegramBotClient botClient,
    IVoiceTranscriptionService voiceService,
    IAudioConverter audioConverter,
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramBotService> logger)
{
    private int? _assistantTopicId;
    private int? _notificationsTopicId;

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        var message = update.Message;
        var chatId = message?.Chat.Id
            ?? update.CallbackQuery?.Message?.Chat.Id ?? 0L;
        if (chatId == 0) return;

        var text = message?.Text
            ?? update.CallbackQuery?.Data;

        // Voice message: download -> OGG-to-WAV -> Whisper transcription
        if (message?.Voice is not null && string.IsNullOrEmpty(text))
        {
            try
            {
                text = await TranscribeVoiceAsync(message.Voice.FileId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Voice transcription failed");
                text = null;
            }
        }

        if (string.IsNullOrEmpty(text)) return;

        var pa = clusterClient.GetGrain<IPersonalAssistant>("personal-assistant");
        var threadId = message?.MessageThreadId ?? _assistantTopicId;

        // Send placeholder, then progressively edit with streamed response
        var sent = await botClient.SendMessageAsync(chatId, "...", messageThreadId: threadId);
        var buffer = new StringBuilder();
        var lastEditAt = DateTimeOffset.MinValue;

        try
        {
            await foreach (var chunk in pa.GetResponseStream(text, ct))
            {
                buffer.Append(chunk);
                if ((DateTimeOffset.UtcNow - lastEditAt).TotalMilliseconds > 500)
                {
                    await EditSafe(chatId, sent.MessageId, buffer.ToString());
                    lastEditAt = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming response from PersonalAssistant");
            buffer.Append("\n\n[Error communicating with assistant]");
        }

        if (buffer.Length > 0)
            await EditSafe(chatId, sent.MessageId, buffer.ToString());
    }

    public async Task SendNotificationAsync(AgentEvent evt, CancellationToken ct)
    {
        var chatId = options.Value.ChatId;
        if (chatId == 0) return;

        await EnsureTopicsAsync(chatId, ct);

        var text = $"*{EscapeMarkdown(evt.EventName)}* from `{evt.SourceAgentId}`\n" +
                   string.Join("\n", evt.Payload.Select(p => $"  {p.Key}: {p.Value}"));

        await botClient.SendMessageAsync(chatId, text,
            messageThreadId: _notificationsTopicId, parseMode: FormatStyles.MarkdownV2);
    }

    private async Task<string> TranscribeVoiceAsync(string fileId, CancellationToken ct)
    {
        var file = await botClient.GetFileAsync(fileId);
        var downloadUrl = $"{botClient.Options.ServerAddress}/file/bot{options.Value.BotToken}/{file.FilePath}";

        using var http = httpClientFactory.CreateClient();
        await using var oggStream = await http.GetStreamAsync(downloadUrl, ct);
        var wavPath = await audioConverter.ConvertOggToWavAsync(oggStream, ct);
        return await voiceService.TranscribeAsync(wavPath, ct);
    }

    private async Task EnsureTopicsAsync(long chatId, CancellationToken ct)
    {
        if (_assistantTopicId is not null) return;

        try
        {
            var assistantTopic = await botClient.CreateForumTopicAsync(chatId, "Assistant");
            _assistantTopicId = assistantTopic.MessageThreadId;

            var notifTopic = await botClient.CreateForumTopicAsync(chatId, "Notifications");
            _notificationsTopicId = notifTopic.MessageThreadId;
        }
        catch (BotRequestException ex) when (ex.Message.Contains("TOPIC_NAME_ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            // Topics already exist — acceptable, IDs will be null (messages go to general thread)
            logger.LogInformation("Forum topics already exist, using general thread");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create forum topics — chat may not be a supergroup");
        }
    }

    private async Task EditSafe(long chatId, int messageId, string text)
    {
        try
        {
            await botClient.EditMessageTextAsync(chatId, messageId, text);
        }
        catch (BotRequestException ex) when (ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase))
        {
            // Telegram rejects edits with identical text — safe to ignore
        }
    }

    private static string EscapeMarkdown(string text) =>
        text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[")
            .Replace("]", "\\]").Replace("(", "\\(").Replace(")", "\\)")
            .Replace("~", "\\~").Replace("`", "\\`").Replace(">", "\\>")
            .Replace("#", "\\#").Replace("+", "\\+").Replace("-", "\\-")
            .Replace("=", "\\=").Replace("|", "\\|").Replace("{", "\\{")
            .Replace("}", "\\}").Replace(".", "\\.").Replace("!", "\\!");
}
```

Key design decisions:
- Uses `IClusterClient` (not `IGrainFactory`) — this is a CLIENT, not a silo
- Downloads voice files via HTTP (Telegram Bot API file download URL pattern)
- Progressive message edits throttled to 500ms to avoid Telegram rate limits
- `EditSafe` swallows "message is not modified" errors from Telegram
- `EnsureTopicsAsync` creates forum topics on first use, handles already-exists gracefully
- `EscapeMarkdown` handles MarkdownV2 escaping for notification messages

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Clients.Telegram/Telegram.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Clients.Telegram/TelegramBotService.cs
git commit -m "feat: add TelegramBotService with streaming responses and voice support"
```

---

### Task 6: Write StreamSubscriber and Program.cs

**Files:**
- Create: `src/Clients.Telegram/StreamSubscriber.cs`
- Create: `src/Clients.Telegram/Program.cs`

- [ ] **Step 1: Write StreamSubscriber.cs**

Subscribes to Orleans notification stream. Matches the `StreamId.Create("agents", "notification.sent")` pattern used by `NotificationAgent.SendNotification` (see `src/Agents/Orchestration/NotificationAgent.cs:37`):

```csharp
using Core.Contracts;
using Orleans.Streams;

namespace TelegramClient;

public sealed class StreamSubscriber(
    IClusterClient clusterClient,
    TelegramBotService botService,
    ILogger<StreamSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Wait for Orleans client to connect
        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        try
        {
            var streamProvider = clusterClient.GetStreamProvider("agents");
            var stream = streamProvider.GetStream<AgentEvent>(
                StreamId.Create("agents", "notification.sent"));

            await stream.SubscribeAsync(async (evt, token) =>
            {
                try
                {
                    await botService.SendNotificationAsync(evt, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send notification to Telegram");
                }
            });

            logger.LogInformation("Subscribed to agent notification stream");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to notification stream");
        }

        // Keep alive
        await Task.Delay(Timeout.Infinite, ct);
    }
}
```

- [ ] **Step 2: Write Program.cs**

Same Orleans client pattern as DevUI (`src/DevUI/Program.cs`) and MCP (`src/IAW.MCP/Program.cs`):

```csharp
using System.Net;
using Microsoft.Extensions.Options;
using ServiceDefaults;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;
using TelegramClient;
using TelegramClient.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Orleans CLIENT — same pattern as DevUI and MCP
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
        var header = context.Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();
        if (!string.Equals(header, secret, StringComparison.Ordinal))
            return Results.Unauthorized();
    }

    var update = await context.Request.ReadFromJsonAsync<Update>(ct);
    if (update is null)
        return Results.BadRequest();

    await botService.HandleUpdateAsync(update, ct);
    return Results.Ok();
});

app.MapGet("/", () => "IAW Telegram Client");
app.Run();
```

- [ ] **Step 3: Verify Telegram client builds**

Run: `dotnet build src/Clients.Telegram/Telegram.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add src/Clients.Telegram/StreamSubscriber.cs src/Clients.Telegram/Program.cs
git commit -m "feat: add Telegram Program.cs and StreamSubscriber"
```

---

### Task 7: Write WebhookSetupService

**Files:**
- Create: `src/Clients.Telegram/WebhookSetupService.cs`

Registers the webhook URL with Telegram's servers on startup. The old version called `ITelegramConversation.SetWebhook` (deleted grain). The new version calls `ITelegramBotClient.SetWebhookAsync` directly.

- [ ] **Step 1: Write WebhookSetupService.cs**

```csharp
using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;

namespace TelegramClient;

public sealed class WebhookSetupService(
    ITelegramBotClient botClient,
    IOptions<TelegramBotOptions> options,
    ILogger<WebhookSetupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            logger.LogWarning("No webhook URL configured — Telegram bot will not receive updates");
            return;
        }

        try
        {
            await botClient.SetWebhookAsync(
                config.WebhookUrl,
                secretToken: string.IsNullOrWhiteSpace(config.WebhookSecretToken) ? null : config.WebhookSecretToken,
                cancellationToken: ct);

            logger.LogInformation("Webhook registered: {Url}", config.WebhookUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set webhook at {Url}", config.WebhookUrl);
        }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Clients.Telegram/Telegram.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add src/Clients.Telegram/WebhookSetupService.cs
git commit -m "feat: add WebhookSetupService to register webhook with Telegram API"
```

---

### Task 8: Wire Telegram into AppHost and solution

**Files:**
- Modify: `src/IAW.AppHost/Aspire.csproj`
- Modify: `src/IAW.AppHost/AppHost.cs`
- Modify: `IAW.slnx`

- [ ] **Step 1: Add Telegram project reference to Aspire.csproj**

Add to the project references `<ItemGroup>`:
```xml
<ProjectReference Include="..\Clients.Telegram\Telegram.csproj" />
```

- [ ] **Step 2: Add Telegram client to AppHost.cs**

Add before `builder.Build().Run();`:

```csharp
// Telegram client
var botToken = builder.AddParameter("bot-token", secret: true);
builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(iaw.AsClient())
    .WithEnvironment("Orleans__PrimaryGateway", assistant.GetEndpoint("orleans-gateway"))
    .WithEnvironment("Telegram__BotToken", botToken)
    .WaitFor(assistant);
```

- [ ] **Step 3: Add Telegram to IAW.slnx**

Add to the `/src/` folder section:
```xml
<Project Path="src/Clients.Telegram/Telegram.csproj" />
```

- [ ] **Step 4: Verify full solution builds**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/IAW.AppHost/Aspire.csproj src/IAW.AppHost/AppHost.cs IAW.slnx
git commit -m "feat: wire Telegram client into AppHost and solution"
```

---

## Chunk 3: Dead Code Cleanup

### Task 9: Delete dead Telegram V2 files

**Files:**
- Delete: `src/Clients.Telegram.Bot/` (entire directory)

- [ ] **Step 1: Delete the entire old Telegram.Bot directory**

```bash
rm -rf src/Clients.Telegram.Bot
```

This removes:
- `TelegramConversation.cs` — V2 grain replaced by PA routing
- `ITelegramConversation.cs` — V2 interface and serializable models
- `AgentRouter.cs` — PA has built-in `AssignTaskToAgent`
- `MonitorSourceProvider.cs` — unused RSS monitoring grain
- `WebhookSetupService.cs` — old version calling `ITelegramConversation.SetWebhook`
- `Services/VoiceCallService.cs` — placeholder stub with no implementation
- `Program.cs` — old silo Program.cs
- `TelegramBot.csproj` — old csproj
- `Services/AudioConverter.cs` — already copied to new location
- `Services/VoiceTranscriptionService.cs` — already copied to new location
- `appsettings.json` — old config

- [ ] **Step 2: Commit**

```bash
git add -A src/Clients.Telegram.Bot/
git commit -m "cleanup: delete V2 Telegram.Bot silo — replaced by Clients.Telegram client"
```

---

### Task 10: Delete dead Core interfaces

**Files:**
- Delete: `src/Core/Routing/IAgentRouter.cs`
- Delete: `src/Core/Contracts/IMonitorSourceProvider.cs` (contains `IMonitorSourceProvider`, `MonitorPollRequest`, `MonitorPollResult`, `MonitorFeedItem`)

- [ ] **Step 1: Delete IAgentRouter**

Only referenced by the now-deleted `AgentRouter.cs` and `TelegramConversation.cs`.

```bash
rm src/Core/Routing/IAgentRouter.cs
```

If `src/Core/Routing/` is now empty, remove the directory too:
```bash
rmdir src/Core/Routing 2>/dev/null || true
```

- [ ] **Step 2: Delete IMonitorSourceProvider and its models**

Only referenced by the now-deleted `MonitorSourceProvider.cs` and `TelegramConversation.cs`.

```bash
rm src/Core/Contracts/IMonitorSourceProvider.cs
```

- [ ] **Step 3: Verify Core still builds**

Run: `dotnet build src/Core/Core.csproj`
Expected: Build succeeded (no remaining references to deleted types)

- [ ] **Step 4: Verify full solution builds**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add src/Core/Routing/ src/Core/Contracts/IMonitorSourceProvider.cs
git commit -m "cleanup: delete dead IAgentRouter and IMonitorSourceProvider interfaces"
```

---

### Task 11: Final verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build IAW.slnx`
Expected: Build succeeded, 0 warnings related to missing types

- [ ] **Step 2: Run all tests**

Run: `dotnet test IAW.slnx`
Expected: All tests pass

- [ ] **Step 3: Run Aspire**

Run: `aspire run --project src/IAW.AppHost/Aspire.csproj`
Expected: All resources start green:
- `assistant` — running (production silo)
- `samples` — running (demo silo)
- `devui` — running (connected to assistant)
- `mcp` — running (connected to assistant)
- `telegram` — running (connected to assistant, will fail gracefully without bot token configured)
- `ollama` — running

- [ ] **Step 4: Verify DevUI chat works**

Open DevUI in browser, send a message to PersonalAssistant.
Expected: Response received (confirms clients correctly point to assistant silo)

- [ ] **Step 5: Verify memory works cross-channel**

In DevUI, tell PersonalAssistant "My birthday is March 15".
Start a new conversation, ask "When is my birthday?"
Expected: PersonalAssistant recalls "March 15" (memory context provider working)

- [ ] **Step 6: Commit if any final fixes were needed**

```bash
git add -A
git commit -m "fix: final adjustments from integration verification"
```
