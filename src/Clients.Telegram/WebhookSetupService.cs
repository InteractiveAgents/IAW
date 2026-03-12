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
