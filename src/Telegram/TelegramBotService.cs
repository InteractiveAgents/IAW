using Core;
using Core.Contracts;
using Core.Contracts.UI;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using TelegramClient.Services;

namespace TelegramClient;

public sealed class TelegramBotService
{
    readonly IClusterClient _clusterClient;
    readonly TelegramMessageSender _messageSender;
    readonly TelegramFileService _fileService;
    readonly CommandHandler _commandHandler;
    readonly CallbackRouter _callbackRouter;
    readonly ResponseStreamer _responseStreamer;
    readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(
        IClusterClient clusterClient,
        TelegramMessageSender messageSender,
        TelegramFileService fileService,
        CommandHandler commandHandler,
        CallbackRouter callbackRouter,
        ResponseStreamer responseStreamer,
        ILogger<TelegramBotService> logger)
    {
        _clusterClient = clusterClient;
        _messageSender = messageSender;
        _fileService = fileService;
        _commandHandler = commandHandler;
        _callbackRouter = callbackRouter;
        _responseStreamer = responseStreamer;
        _logger = logger;

        _callbackRouter.StreamResponse = (chatId, msgId, topicId, thread, msg, telegramId, ct, slug) =>
            _responseStreamer.StreamAsync(chatId, msgId, topicId, thread, msg, telegramId, ct, slug);
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        try
        {
            await HandleUpdateCoreAsync(update, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in HandleUpdateAsync");
        }
    }

    async Task HandleUpdateCoreAsync(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callbackQuery)
        {
            await _callbackRouter.HandleAsync(callbackQuery, ct);
            return;
        }

        var message = update.Message;
        if (message is null) return;

        var chatId = message.Chat.Id;
        if (chatId == 0) return;

        var from = message.From;
        if (from is null) return;

        await _messageSender.SetReactionAsync(chatId, message.MessageId, "\ud83d\udc40");

        var text = message.Text;

        if (message.Voice is not null && string.IsNullOrEmpty(text))
        {
            try
            {
                text = await _fileService.TranscribeVoiceAsync(message.Voice.FileId, ct);
                if (!string.IsNullOrEmpty(text))
                    await _messageSender.SendTextAsync(chatId, $"\ud83c\udfA4 {text}", message.MessageThreadId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Voice transcription failed");
                text = null;
            }
        }

        var telegramId = from.Id;
        var topicId = message.MessageThreadId;

        var userProfile = _clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
        var prefs = await userProfile.GetPreferences(ct);
        if (!prefs.TryGetValue(IAWConstants.StateKeys.GroupChatId, out var storedChatId) || storedChatId != chatId.ToString())
            await userProfile.SetPreference(IAWConstants.StateKeys.GroupChatId, chatId.ToString(), ct);

        if (message.Photo is not null && message.Photo.Any())
        {
            await HandlePhotoAsync(message, telegramId, topicId, ct);
            return;
        }

        if (message.Document is not null)
        {
            await HandleDocumentAsync(message, telegramId, topicId, ct);
            return;
        }

        if (string.IsNullOrEmpty(text)) return;

        if (text.StartsWith("/"))
        {
            await _commandHandler.HandleAsync(chatId, from.Id, topicId, text, ct);
            return;
        }

        var topicKey = topicId?.ToString() ?? "general";
        var session = _clusterClient.GetGrain<IUISession>(telegramId.ToString());
        if (await session.HasPendingFreeTextInput(topicKey, ct))
        {
            // future: route to UISession free-text handler
        }

        var (thread, slug) = await ThreadResolver.ResolveAsync(_clusterClient, telegramId, topicId, ct);
        var chatMessage = ChatMessageBuilder.FromText(text);

        _logger.LogInformation("Processing message from user {TelegramId} in topic {TopicId}: {Text}",
            telegramId, topicId, text);
        var sent = await _messageSender.SendTextAsync(chatId, "...", topicId);
        await _responseStreamer.StreamAsync(chatId, sent.MessageId, topicId, thread, chatMessage, telegramId, ct, slug,
            userMessageId: message.MessageId);
    }

    async Task HandlePhotoAsync(Message message, long telegramId, int? topicId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var highestResPhoto = message.Photo!.Last();

        _logger.LogInformation("Processing photo from user {TelegramId}, file {FileId}", telegramId, highestResPhoto.FileId);
        var sent = await _messageSender.SendTextAsync(chatId, "Processing image...", topicId);

        try
        {
            await using var photoStream = await _fileService.DownloadTelegramFileAsync(highestResPhoto.FileId, ct);

            var (thread, threadSlug) = await ThreadResolver.ResolveAsync(_clusterClient, telegramId, topicId, ct);
            var blobPath = $"{telegramId}/{threadSlug}/{Guid.NewGuid()}-photo.jpg";
            var blobUri = await _fileService.UploadToBlobAsync(photoStream, blobPath, "image/jpeg");

            var chatMessage = new ChatMessage
            {
                Role = "user",
                Parts = [new ImageContent(blobUri, "image/jpeg", message.Caption)]
            };

            await _responseStreamer.StreamAsync(chatId, sent.MessageId, topicId, thread, chatMessage, telegramId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Photo processing failed for user {TelegramId}", telegramId);
            await _messageSender.EditTextAsync(chatId, sent.MessageId, "[Error processing image]");
        }
    }

    async Task HandleDocumentAsync(Message message, long telegramId, int? topicId, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        var document = message.Document!;

        _logger.LogInformation("Processing document from user {TelegramId}, file {FileName}", telegramId, document.FileName);
        var sent = await _messageSender.SendTextAsync(chatId, "Processing document...", topicId);

        try
        {
            await using var docStream = await _fileService.DownloadTelegramFileAsync(document.FileId, ct);

            var (thread, threadSlug) = await ThreadResolver.ResolveAsync(_clusterClient, telegramId, topicId, ct);
            var safeFileName = document.FileName ?? "document";
            var blobPath = $"{telegramId}/{threadSlug}/{Guid.NewGuid()}-{safeFileName}";
            var mimeType = document.MimeType ?? "application/octet-stream";

            var blobUri = await _fileService.UploadToBlobAsync(docStream, blobPath, mimeType);

            var chatMessage = new ChatMessage
            {
                Role = "user",
                Parts = [new FileContent(blobUri, safeFileName, mimeType, document.FileSize ?? 0, Ingested: false)]
            };

            if (!string.IsNullOrEmpty(message.Caption))
                chatMessage.Parts.Add(new TextContent(message.Caption));

            await _responseStreamer.StreamAsync(chatId, sent.MessageId, topicId, thread, chatMessage, telegramId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document processing failed for user {TelegramId}", telegramId);
            await _messageSender.EditTextAsync(chatId, sent.MessageId, "[Error processing document]");
        }
    }

    // public accessors for file delivery (used by other services)
    public async Task SendDocumentAsync(long chatId, Stream fileStream, string fileName, string? caption, int? topicId, CancellationToken ct)
    {
        var inputFile = new InputFile(fileStream, fileName);
        await _messageSender.SendDocumentAsync(chatId, inputFile, topicId, caption);
    }

    public async Task SendPhotoAsync(long chatId, Stream photoStream, string fileName, string? caption, int? topicId, CancellationToken ct)
    {
        var inputFile = new InputFile(photoStream, fileName);
        await _messageSender.SendPhotoAsync(chatId, inputFile, topicId, caption);
    }
}
