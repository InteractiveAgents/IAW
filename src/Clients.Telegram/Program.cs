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
        try { await botService.HandleUpdateAsync(update, CancellationToken.None); }
        catch (Exception ex) { logger.LogError(ex, "Background update processing failed"); }
    });
    return Results.Ok();
});

app.MapGet("/", () => "IAW Telegram Client");
app.Run();
