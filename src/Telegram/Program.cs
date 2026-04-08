using Aspire.IAW;
using Core.AI;
using Core.Services;
using Microsoft.Extensions.Options;
using Telegram;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;
using TelegramClient;
using TelegramClient.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddIAWClient();

builder.AddAzureBlobServiceClient("file-storage");

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
builder.AddWhisperProvider<FoundryLocalTranscriptionService>();
builder.Services.AddSingleton<BlobFileStorage>();

var app = builder.Build();
app.MapDefaultEndpoints();

app.MapPost("/webhook", async (
    HttpContext context,
    TelegramBotService botService,
    IOptions<TelegramBotOptions> options,
    ILogger<Program> logger,
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

    // Fire-and-forget — return 200 to Telegram immediately,
    // process the update in the background so webhook doesn't timeout
    _ = Task.Run(async () =>
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try { await botService.HandleUpdateAsync(update, cts.Token); }
        catch (OperationCanceledException) { logger.LogWarning("Update processing timed out after 5 minutes"); }
        catch (Exception ex) { logger.LogError(ex, "Background update processing failed"); }
    }, ct);
    return Results.Ok();
});

app.MapGet("/", () => "IAW Telegram Client");
app.Run();