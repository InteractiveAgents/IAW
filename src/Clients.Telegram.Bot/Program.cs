using IAW.Core;
using Core.AI;
using Microsoft.Extensions.Options;
using Orleans.Journaling;
using ServiceDefaults;
using System.Diagnostics;
using Telegram.BotAPI;
using TelegramBot;
using TelegramBot.Services;
using BotUpdate = Telegram.BotAPI.GettingUpdates.Update;

var builder = WebApplication.CreateBuilder(args);

var siloPort = builder.Configuration.GetValue("Orleans:Endpoints:SiloPort", 11_112);
var gatewayPort = builder.Configuration.GetValue("Orleans:Endpoints:GatewayPort", 30_001);
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
});

builder.AddLlmProviders();
builder.AddEmbeddingProvider();
builder.AddQdrantClient("qdrant");

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection("Telegram"));

builder.Services.AddSingleton<ITelegramBotClient>(sp =>
{
    var config = sp.GetRequiredService<IOptions<TelegramBotOptions>>().Value;
    return new TelegramBotClient(config.BotToken);
});

builder.Services.AddHttpClient();
builder.Services.AddHostedService<WebhookSetupService>();
builder.Services.AddSingleton<IAudioConverter, AudioConverter>();
builder.Services.AddSingleton<IVoiceTranscriptionService, VoiceTranscriptionService>();
builder.Services.AddSingleton<IVoiceCallService, VoiceCallService>();
builder.AddServiceDefaults();

var app = builder.Build();
app.MapDefaultEndpoints();

const string webhookSecretHeader = "X-Telegram-Bot-Api-Secret-Token";

app.MapPost("/webhook", async (
    HttpContext context,
    IGrainFactory grains,
    IOptions<TelegramBotOptions> options,
    CancellationToken ct) =>
{
    var secret = options.Value.WebhookSecretToken;
    if (!string.IsNullOrWhiteSpace(secret))
    {
        var headerValue = context.Request.Headers[webhookSecretHeader].FirstOrDefault();
        if (!string.Equals(headerValue, secret, StringComparison.Ordinal))
            return Results.Unauthorized();
    }

    var update = await context.Request.ReadFromJsonAsync<BotUpdate>(ct);
    if (update is null)
        return Results.BadRequest();

    var chatId = update.Message?.Chat.Id
        ?? update.CallbackQuery?.Message?.Chat.Id
        ?? 0L;

    if (chatId == 0)
        return Results.Ok();

    var currentActivity = Activity.Current;
    var botUpdate = new TelegramBotUpdate
    {
        ChatId = chatId,
        MessageId = update.Message?.MessageId ?? 0,
        ThreadId = update.Message?.MessageThreadId,
        Text = update.Message?.Text,
        CallbackQueryId = update.CallbackQuery?.Id,
        CallbackData = update.CallbackQuery?.Data,
        Username = update.Message?.From?.Username ?? update.CallbackQuery?.From?.Username,
        FirstName = update.Message?.From?.FirstName ?? update.CallbackQuery?.From?.FirstName,
        FromUserId = update.Message?.From?.Id ?? update.CallbackQuery?.From?.Id,
        VoiceFileId = update.Message?.Voice?.FileId,
        VoiceDuration = update.Message?.Voice?.Duration ?? 0,
        CorrelationId = currentActivity?.TraceId.ToHexString() ?? context.TraceIdentifier,
        TraceId = currentActivity?.TraceId.ToHexString(),
        ParentSpanId = currentActivity?.SpanId.ToHexString(),
        TraceSampled = currentActivity?.Recorded ?? false
    };

    var conversation = grains.GetGrain<ITelegramConversation>($"conversation-{chatId}");
    _ = conversation.HandleUpdate(botUpdate, ct);

    return Results.Ok();
});

app.MapGet("/", () => "IAW Telegram Bot");
app.Run();
