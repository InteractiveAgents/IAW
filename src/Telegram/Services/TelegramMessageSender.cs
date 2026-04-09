using Microsoft.Extensions.Options;
using Telegram;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.UpdatingMessages;

namespace TelegramClient.Services;

public sealed class TelegramMessageSender(
    ITelegramBotClient botClient,
    TelegramRateLimiter rateLimiter,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramMessageSender> logger)
{
    public ITelegramBotClient BotClient => botClient;
    public string BotToken => options.Value.BotToken;
    public string ServerAddress => botClient.Options.ServerAddress;

    public async Task<Message> SendTextAsync(long chatId, string text, int? topicId = null,
        InlineKeyboardMarkup? replyMarkup = null, string? parseMode = null)
    {
        await rateLimiter.AcquireAsync(chatId);
        return await botClient.SendMessageAsync(chatId, text,
            messageThreadId: topicId, replyMarkup: replyMarkup, parseMode: parseMode);
    }

    public async Task EditTextAsync(long chatId, int messageId, string text,
        InlineKeyboardMarkup? replyMarkup = null, string? parseMode = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        await rateLimiter.AcquireAsync(chatId);

        if (parseMode is not null)
        {
            try
            {
                await botClient.EditMessageTextAsync(chatId, messageId, text,
                    parseMode: parseMode, replyMarkup: replyMarkup);
                return;
            }
            catch (BotRequestException)
            {
                // formatted parse failed — fall through to plain text
            }
        }

        try
        {
            await botClient.EditMessageTextAsync(chatId, messageId, text, replyMarkup: replyMarkup);
        }
        catch (BotRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("message text is empty", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    public async Task SendDocumentAsync(long chatId, InputFile file, int? topicId = null, string? caption = null)
    {
        await rateLimiter.AcquireAsync(chatId);
        await botClient.SendDocumentAsync(chatId, file, messageThreadId: topicId, caption: caption);
    }

    public async Task SendPhotoAsync(long chatId, InputFile file, int? topicId = null, string? caption = null)
    {
        await rateLimiter.AcquireAsync(chatId);
        await botClient.SendPhotoAsync(chatId, file, messageThreadId: topicId, caption: caption);
    }

    public async Task SendMediaGroupAsync(long chatId, IEnumerable<InputMedia> media, int? topicId = null)
    {
        await rateLimiter.AcquireAsync(chatId);
        await botClient.SendMediaGroupAsync(chatId, media, messageThreadId: topicId);
    }

    public async Task SetReactionAsync(long chatId, int messageId, string emoji)
    {
        try
        {
            await rateLimiter.AcquireAsync(chatId);
            await botClient.SetMessageReactionAsync(chatId, messageId, [new ReactionTypeEmoji(emoji)]);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to set reaction {Emoji}", emoji);
        }
    }

    public async Task AnswerCallbackAsync(string callbackQueryId, string? text = null)
    {
        try
        {
            await botClient.AnswerCallbackQueryAsync(callbackQueryId, text: text);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to answer callback query");
        }
    }

    // HTML convenience

    public Task<Message> SendHtmlAsync(long chatId, string html, int? topicId = null,
        InlineKeyboardMarkup? replyMarkup = null)
        => SendTextAsync(chatId, html, topicId, replyMarkup, "HTML");

    public Task EditHtmlAsync(long chatId, int messageId, string html,
        InlineKeyboardMarkup? replyMarkup = null)
        => EditTextAsync(chatId, messageId, html, replyMarkup, "HTML");

    // forum topic operations

    public Task<ForumTopic> CreateTopicAsync(long chatId, string name, int? iconColor = null)
        => botClient.CreateForumTopicAsync(chatId, name, iconColor: iconColor);

    public Task EditTopicAsync(long chatId, int topicId, string name)
        => botClient.EditForumTopicAsync(chatId, topicId, name: name);

    public Task DeleteTopicAsync(long chatId, int topicId)
        => botClient.DeleteForumTopicAsync(chatId, topicId);

    public Task PinMessageAsync(long chatId, int messageId)
        => botClient.PinChatMessageAsync(chatId, messageId);
}
