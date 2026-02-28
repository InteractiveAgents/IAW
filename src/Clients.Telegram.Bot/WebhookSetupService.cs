using Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;

namespace TelegramBot;

public sealed class TelegramBotOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public long OwnerChatId { get; set; }
}

public sealed class WebhookSetupService(
    IGrainFactory grains,
    IOptions<TelegramBotOptions> options,
    ILogger<WebhookSetupService> logger) : BackgroundService
{
    private const int MaxRetries = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            logger.LogWarning("No webhook URL configured, skipping webhook setup");
            return;
        }

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var bot = grains.GetGrain<ITelegramBot>("bot");
                await bot.SetWebhook(config.WebhookUrl, config.WebhookSecretToken, stoppingToken);
                logger.LogInformation("Webhook registered on attempt {Attempt}: {Url}", attempt, config.WebhookUrl);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Webhook setup attempt {Attempt}/{Max} failed", attempt, MaxRetries);
                if (attempt < MaxRetries)
                    await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogError("Failed to register webhook after {Max} attempts", MaxRetries);
    }
}
