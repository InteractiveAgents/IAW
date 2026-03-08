using Core;
using Orleans.Concurrency;

namespace TelegramBot;

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

[GenerateSerializer]
public sealed class TelegramBotUpdate
{
    [Id(0)] public long ChatId { get; set; }
    [Id(1)] public int MessageId { get; set; }
    [Id(2)] public int? ThreadId { get; set; }
    [Id(3)] public string? Text { get; set; }
    [Id(4)] public string? CallbackQueryId { get; set; }
    [Id(5)] public string? CallbackData { get; set; }
    [Id(6)] public string? Username { get; set; }
    [Id(7)] public string? FirstName { get; set; }
    [Id(8)] public long? FromUserId { get; set; }
    [Id(9)] public string? VoiceFileId { get; set; }
    [Id(10)] public int VoiceDuration { get; set; }
    [Id(11)] public string? CorrelationId { get; set; }
    [Id(12)] public string? TraceId { get; set; }
    [Id(13)] public string? ParentSpanId { get; set; }
    [Id(14)] public bool TraceSampled { get; set; }
}

[GenerateSerializer]
public sealed class TelegramSendResult
{
    [Id(0)] public bool Success { get; set; }
    [Id(1)] public int MessageId { get; set; }
    [Id(2)] public string? Error { get; set; }

    public static TelegramSendResult Ok(int messageId) => new() { Success = true, MessageId = messageId };
    public static TelegramSendResult Fail(string error) => new() { Success = false, Error = error };
}

[GenerateSerializer]
public sealed class TelegramInlineButton
{
    [Id(0)] public string Text { get; set; } = string.Empty;
    [Id(1)] public string CallbackData { get; set; } = string.Empty;
}

[GenerateSerializer]
public sealed class TelegramTopicRegistry
{
    [Id(0)] public int AssistantThreadId { get; set; }
    [Id(1)] public int NotificationsThreadId { get; set; }
    [Id(2)] public int SettingsThreadId { get; set; }
    [Id(3)] public Dictionary<string, int> TaskTopics { get; set; } = [];
    [Id(4)] public int TeamThreadId { get; set; }
}
