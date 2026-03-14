using System.Text;
using Core.Contracts;
using Core.Contracts.UI;
using Core.Services;
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
    BlobFileStorage blobFileStorage,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramBotService> logger)
{
    private int? _assistantTopicId;
    private int? _notificationsTopicId;

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        try
        {
            await HandleUpdateCoreAsync(update, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in HandleUpdateAsync");
        }
    }

    private async Task HandleUpdateCoreAsync(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQueryAsync(callbackQuery, ct);
            return;
        }

        var message = update.Message;
        if (message is null) return;

        var chatId = message.Chat.Id;
        if (chatId == 0) return;

        var from = message.From;
        if (from is null) return;

        var text = message.Text;

        // Voice message: download -> OGG-to-WAV -> Whisper transcription
        if (message.Voice is not null && string.IsNullOrEmpty(text))
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

        var telegramId = from.Id;
        var topicId = message.MessageThreadId;

        // Photo message: download highest-res photo -> upload to blob -> send as ImageContent
        if (message.Photo is not null && message.Photo.Any())
        {
            await HandlePhotoAsync(message, telegramId, topicId, ct);
            return;
        }

        // Document message: download -> upload to blob -> send as FileContent
        if (message.Document is not null)
        {
            await HandleDocumentAsync(message, telegramId, topicId, ct);
            return;
        }

        if (string.IsNullOrEmpty(text)) return;

        // Check UISession for pending free-text input (placeholder for Slice 6)
        var topicKey = topicId?.ToString() ?? "general";
        var session = clusterClient.GetGrain<IUISession>(telegramId.ToString());
        if (await session.HasPendingFreeTextInput(topicKey, ct))
        {
            // Future: route to UISession free-text handler
        }

        var (project, _) = await ResolveProjectAsync(telegramId, topicId, ct);
        var chatMessage = BuildChatMessage(text);

        logger.LogInformation("Processing message from user {TelegramId} in topic {TopicId}: {Text}",
            telegramId, topicId, text);
        var sent = await botClient.SendMessageAsync(chatId, "...", messageThreadId: topicId);
        await StreamResponseAsync(chatId, sent.MessageId, project, chatMessage, telegramId, ct);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var from = callbackQuery.From;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0L;
        if (chatId == 0) return;

        var session = clusterClient.GetGrain<IUISession>(from.Id.ToString());
        var result = await session.HandleCallback(callbackQuery.Id, callbackQuery.Data ?? "", ct);

        try
        {
            await botClient.AnswerCallbackQueryAsync(callbackQuery.Id, text: result.Toast);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to answer callback query");
        }

        if (result.NewText is not null && callbackQuery.Message is not null)
        {
            await EditSafe(chatId, callbackQuery.Message.MessageId, result.NewText);
        }
    }

    private async Task<(IProject Project, string Slug)> ResolveProjectAsync(long telegramId, int? topicId, CancellationToken ct)
    {
        var userProfileId = telegramId.ToString();
        var userProfile = clusterClient.GetGrain<IUserProfile>(userProfileId);
        var topicKey = topicId?.ToString() ?? "general";

        var projectSlug = await userProfile.ResolveProject(topicKey, ct);
        if (projectSlug is null)
        {
            projectSlug = topicId is null ? "general" : $"topic-{topicId}";
            await userProfile.RegisterProject(projectSlug, topicKey, ct);
        }

        var grainId = $"{userProfileId}/{projectSlug}";
        return (clusterClient.GetGrain<IProject>(grainId), projectSlug);
    }

    private static ChatMessage BuildChatMessage(string text) => new()
    {
        Role = "user",
        Parts = new List<ContentPart> { new TextContent(text) }
    };

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

    public async Task SendApprovalAsync(string approvalId, string question, string[] approvalOptions, string projectSlug, CancellationToken ct)
    {
        var chatId = options.Value.ChatId;
        if (chatId == 0) return;

        var buttons = approvalOptions.Select(opt =>
            new InlineKeyboardButton(opt) { CallbackData = $"ap:{approvalId}:{opt}" }
        ).ToArray();
        var keyboard = new InlineKeyboardMarkup([buttons]);

        var telegramId = projectSlug.Split('/')[0];
        var session = clusterClient.GetGrain<IUISession>(telegramId);
        await session.RegisterApproval(approvalId, question, approvalOptions, projectSlug, ct);

        await botClient.SendMessageAsync(chatId, $"\ud83d\udd14 {question}", replyMarkup: keyboard);
    }

    private async Task HandlePhotoAsync(Message message, long telegramId, int? topicId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var highestResPhoto = message.Photo!.Last(); // last element is highest resolution

        logger.LogInformation("Processing photo from user {TelegramId}, file {FileId}", telegramId, highestResPhoto.FileId);
        var sent = await botClient.SendMessageAsync(chatId, "Processing image...", messageThreadId: topicId);

        try
        {
            await using var photoStream = await DownloadTelegramFileAsync(highestResPhoto.FileId, ct);

            var (project, projectSlug) = await ResolveProjectAsync(telegramId, topicId, ct);
            var blobPath = $"{telegramId}/{projectSlug}/{Guid.NewGuid()}-photo.jpg";

            var blobUri = await blobFileStorage.UploadAsync(photoStream, blobPath, "image/jpeg");

            var chatMessage = new ChatMessage
            {
                Role = "user",
                Parts = [new ImageContent(blobUri, "image/jpeg", message.Caption)]
            };

            await StreamResponseAsync(chatId, sent.MessageId, project, chatMessage, telegramId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Photo processing failed for user {TelegramId}", telegramId);
            await EditSafe(chatId, sent.MessageId, "[Error processing image]");
        }
    }

    private async Task HandleDocumentAsync(Message message, long telegramId, int? topicId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var document = message.Document!;

        logger.LogInformation("Processing document from user {TelegramId}, file {FileName}", telegramId, document.FileName);
        var sent = await botClient.SendMessageAsync(chatId, "Processing document...", messageThreadId: topicId);

        try
        {
            await using var docStream = await DownloadTelegramFileAsync(document.FileId, ct);

            var (project, projectSlug) = await ResolveProjectAsync(telegramId, topicId, ct);
            var safeFileName = document.FileName ?? "document";
            var blobPath = $"{telegramId}/{projectSlug}/{Guid.NewGuid()}-{safeFileName}";
            var mimeType = document.MimeType ?? "application/octet-stream";

            var blobUri = await blobFileStorage.UploadAsync(docStream, blobPath, mimeType);

            var chatMessage = new ChatMessage
            {
                Role = "user",
                Parts = [new FileContent(blobUri, safeFileName, mimeType, document.FileSize ?? 0, Ingested: false)]
            };

            // Include caption as text if provided
            if (!string.IsNullOrEmpty(message.Caption))
                chatMessage.Parts.Add(new TextContent(message.Caption));

            await StreamResponseAsync(chatId, sent.MessageId, project, chatMessage, telegramId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Document processing failed for user {TelegramId}", telegramId);
            await EditSafe(chatId, sent.MessageId, "[Error processing document]");
        }
    }

    private async Task StreamResponseAsync(
        long chatId, int messageId, IProject project, ChatMessage chatMessage, long telegramId, CancellationToken ct)
    {
        var buffer = new StringBuilder();
        var lastEditAt = DateTimeOffset.MinValue;

        try
        {
            await foreach (var chunk in project.GetResponseStream(chatMessage, ct))
            {
                buffer.Append(chunk);
                if ((DateTimeOffset.UtcNow - lastEditAt).TotalMilliseconds > 500)
                {
                    await EditSafe(chatId, messageId, buffer.ToString());
                    lastEditAt = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming response from project for user {TelegramId}", telegramId);
            buffer.Append("\n\n[Error communicating with assistant]");
        }

        if (buffer.Length > 0)
            await EditSafe(chatId, messageId, buffer.ToString());
    }

    private async Task<Stream> DownloadTelegramFileAsync(string fileId, CancellationToken ct)
    {
        var file = await botClient.GetFileAsync(fileId);
        var downloadUrl = $"{botClient.Options.ServerAddress}/file/bot{options.Value.BotToken}/{file.FilePath}";

        using var http = httpClientFactory.CreateClient();
        var memoryStream = new MemoryStream();
        await using var responseStream = await http.GetStreamAsync(downloadUrl, ct);
        await responseStream.CopyToAsync(memoryStream, ct);
        memoryStream.Position = 0;
        return memoryStream;
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
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            await botClient.EditMessageTextAsync(chatId, messageId, text);
        }
        catch (BotRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("message text is empty", StringComparison.OrdinalIgnoreCase))
        {
            // Safe to ignore: identical text or empty text during streaming warmup
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
