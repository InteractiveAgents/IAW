using Core.AI;
using Core.Services;
using Core.UI;
using Microsoft.Extensions.Options;
using Telegram;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;

namespace TelegramClient.Services;

public sealed class TelegramFileService(
    ITelegramBotClient botClient,
    IOptions<TelegramBotOptions> options,
    IHttpClientFactory httpClientFactory,
    BlobFileStorage blobFileStorage,
    IAudioTranscriptionService transcriptionService,
    TelegramMessageSender messageSender,
    ILogger<TelegramFileService> logger)
{
    public async Task<Stream> DownloadTelegramFileAsync(string fileId, CancellationToken ct)
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

    public async Task<string> TranscribeVoiceAsync(string fileId, CancellationToken ct)
    {
        var file = await botClient.GetFileAsync(fileId);
        var downloadUrl = $"{botClient.Options.ServerAddress}/file/bot{options.Value.BotToken}/{file.FilePath}";

        using var http = httpClientFactory.CreateClient();
        await using var responseStream = await http.GetStreamAsync(downloadUrl, ct);

        var tempPath = Path.Combine(Path.GetTempPath(), $"iaw_voice_{Guid.NewGuid()}.ogg");
        try
        {
            await using (var fileStream = System.IO.File.Create(tempPath))
                await responseStream.CopyToAsync(fileStream, ct);
            return await transcriptionService.TranscribeAsync(tempPath, ct);
        }
        finally
        {
            if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
        }
    }

    public async Task<string> UploadToBlobAsync(Stream content, string blobPath, string mimeType)
    {
        return await blobFileStorage.UploadAsync(content, blobPath, mimeType);
    }

    public async Task DeliverMediaAsync(long chatId, int? topicId, IReadOnlyList<MediaPart> mediaParts)
    {
        if (mediaParts.Count == 0) return;

        // try media group for 2-10 same-type items (all photos or all documents)
        if (mediaParts.Count >= 2 && mediaParts.Count <= 10)
        {
            var allPhotos = mediaParts.All(p => p.MimeType.StartsWith("image/"));
            if (allPhotos)
            {
                try
                {
                    await DeliverAsMediaGroupAsync(chatId, topicId, mediaParts, "photo");
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Media group send failed, falling back to individual sends");
                }
            }
        }

        // individual sends for mixed types, single items, or media group fallback
        foreach (var part in mediaParts)
        {
            try
            {
                await using var stream = await OpenMediaStreamAsync(part);
                var inputFile = new InputFile(stream, part.FileName);

                if (part.MimeType.StartsWith("image/"))
                    await messageSender.SendPhotoAsync(chatId, inputFile, topicId, part.Caption);
                else
                    await messageSender.SendDocumentAsync(chatId, inputFile, topicId, part.Caption);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deliver file {FileName}", part.FileName);
            }
        }
    }

    async Task DeliverAsMediaGroupAsync(long chatId, int? topicId, IReadOnlyList<MediaPart> mediaParts, string mediaType)
    {
        var inputMedia = new List<InputMedia>();

        foreach (var part in mediaParts)
        {
            await using var stream = await OpenMediaStreamAsync(part);
            var memStream = new MemoryStream();
            await stream.CopyToAsync(memStream);
            memStream.Position = 0;

            var attachName = $"attach://{part.FileName}";
            if (mediaType == "photo")
                inputMedia.Add(new InputMediaPhoto(attachName) { Caption = part.Caption });
            else
                inputMedia.Add(new InputMediaDocument(attachName) { Caption = part.Caption });
        }

        await messageSender.SendMediaGroupAsync(chatId, inputMedia, topicId);
    }

    Task<Stream> OpenMediaStreamAsync(MediaPart part)
    {
        if (part.Url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var localPath = new Uri(part.Url).LocalPath;
            return Task.FromResult<Stream>(System.IO.File.OpenRead(localPath));
        }

        return blobFileStorage.DownloadAsync(part.Url);
    }

    public async Task DeliverPendingAsync(long chatId, int? topicId, Func<Task<List<MediaPart>>> getPendingDeliveries)
    {
        try
        {
            var deliveries = await getPendingDeliveries();
            await DeliverMediaAsync(chatId, topicId, deliveries);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get pending deliveries");
        }
    }
}
