namespace TelegramClient;

public sealed class TelegramBotOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
    public string NgrokApiUrl { get; set; } = string.Empty;
    public long ChatId { get; set; }
}
