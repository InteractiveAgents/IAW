using Orleans.Concurrency;

namespace Core;

public interface ITelegramConversation : IAgent
{
    [OneWay]
    Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct = default);

    Task<TelegramSendResult> SendText(long chatId, string text, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> SendMarkdown(long chatId, string markdown, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> SendKeyboard(long chatId, string text, TelegramInlineButton[][] buttons, int? threadId = null, CancellationToken ct = default);
    Task<TelegramSendResult> EditMessage(long chatId, int messageId, string text, TelegramInlineButton[][]? buttons = null, CancellationToken ct = default);
    Task SendTyping(long chatId, int? threadId = null, CancellationToken ct = default);
    Task SetReaction(long chatId, int messageId, string emoji, CancellationToken ct = default);
    Task PinMessage(long chatId, int messageId, int? threadId = null, CancellationToken ct = default);
    Task<int> CreateTopic(long chatId, string name, CancellationToken ct = default);
    Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct = default);
    Task SetWebhook(string url, string? secretToken = null, CancellationToken ct = default);
    Task AnswerCallback(string callbackQueryId, string? text = null, CancellationToken ct = default);
}
