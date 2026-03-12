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
