using System.Text.Json;
using Microsoft.Extensions.Options;
using Telegram;
using Telegram.BotAPI;
using Telegram.BotAPI.GettingUpdates;

namespace TelegramClient;

public sealed class WebhookSetupService(
    ITelegramBotClient botClient,
    IHttpClientFactory httpClientFactory,
    IOptions<TelegramBotOptions> options,
    ILogger<WebhookSetupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var config = options.Value;
        var webhookUrl = config.WebhookUrl;

        if (string.IsNullOrWhiteSpace(webhookUrl) && !string.IsNullOrWhiteSpace(config.NgrokApiUrl))
            webhookUrl = await ResolveNgrokUrlAsync(config.NgrokApiUrl, ct);

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("No webhook URL configured and ngrok not available — Telegram bot will not receive updates");
            return;
        }

        webhookUrl = webhookUrl.TrimEnd('/') + "/webhook";

        try
        {
            await botClient.SetWebhookAsync(
                webhookUrl,
                secretToken: string.IsNullOrWhiteSpace(config.WebhookSecretToken) ? null : config.WebhookSecretToken,
                cancellationToken: ct);

            logger.LogInformation("Webhook registered: {Url}", webhookUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to set webhook at {Url}", webhookUrl);
        }
    }

    private async Task<string?> ResolveNgrokUrlAsync(string ngrokApiUrl, CancellationToken ct)
    {
        // Ngrok may take a few seconds to start — retry with backoff
        using var http = httpClientFactory.CreateClient();
        var apiBase = ngrokApiUrl.TrimEnd('/');

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2 + attempt), ct);
                var json = await http.GetStringAsync($"{apiBase}/api/tunnels", ct);
                using var doc = JsonDocument.Parse(json);

                foreach (var tunnel in doc.RootElement.GetProperty("tunnels").EnumerateArray())
                {
                    var publicUrl = tunnel.GetProperty("public_url").GetString();
                    if (publicUrl is not null && publicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        logger.LogInformation("Resolved ngrok public URL: {Url}", publicUrl);
                        return publicUrl;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug("Waiting for ngrok tunnel (attempt {Attempt}): {Message}", attempt + 1, ex.Message);
            }
        }

        logger.LogWarning("Could not resolve ngrok tunnel URL after retries");
        return null;
    }
}
