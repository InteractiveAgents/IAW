using System.Text.Json;
using IAW.Core;
using Microsoft.Extensions.Options;

namespace TelegramBot;

public sealed class TelegramBotOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public string NgrokApiUrl { get; set; } = string.Empty;
    public long OwnerChatId { get; set; }
}

public sealed class WebhookSetupService(
    IGrainFactory grains,
    IOptions<TelegramBotOptions> options,
    ILogger<WebhookSetupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        var webhookUrl = config.WebhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl))
            webhookUrl = await DiscoverFromNgrok(config.NgrokApiUrl, stoppingToken);

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("No webhook URL configured and ngrok discovery failed, skipping webhook setup");
            return;
        }

        var bot = grains.GetGrain<ITelegramConversation>("bot-webhook");
        await bot.SetWebhook(webhookUrl, config.WebhookSecretToken, stoppingToken);
        logger.LogInformation("Webhook registered: {Url}", webhookUrl);
    }

    async Task<string?> DiscoverFromNgrok(string ngrokApiUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ngrokApiUrl))
            return null;

        // wait for ngrok to be ready
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        try
        {
            // Use a direct short-timeout client here to avoid noisy resilience retries
            // when ngrok is intentionally unavailable in local/dev runs.
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            var json = await http.GetFromJsonAsync<JsonElement>(
                $"{ngrokApiUrl.TrimEnd('/')}/api/tunnels", ct);

            foreach (var tunnel in json.GetProperty("tunnels").EnumerateArray())
            {
                var publicUrl = tunnel.GetProperty("public_url").GetString();
                if (publicUrl?.StartsWith("https://") == true)
                {
                    logger.LogInformation("Discovered ngrok tunnel: {Url}", publicUrl);
                    return $"{publicUrl}/webhook";
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to discover webhook URL from ngrok at {NgrokApiUrl}", ngrokApiUrl);
        }

        return null;
    }
}
