using Core;
using Core.AI;
using Core.AI.Models;
using Core.Routing;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Text;
using System.Text.Json;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using Telegram.BotAPI.UpdatingMessages;

namespace TelegramBot;

public sealed class TelegramConversationGrain(
    [Memory("agent-values")] IDurableDictionary<string, string> values,
    [Memory("agent-history")] IDurableList<AgentHistoryEntry> history,
    [Memory("agent-events")] IDurableList<AgentEventRecord> events,
    [Memory("agent-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("agent-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("agent-tracking")] IDurableDictionary<string, AgentTrackingStatus> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    ITelegramBotClient bot,
    ILogger<TelegramConversationGrain> logger)
    : Agent(values, history, events, subscriptions, notifications, tracking),
      Core.ITelegramConversation
{
    private const string TopicRegistryStateKey = "telegram:topic-registry";
    private const string StartCommand = "/start";

    public override string DisplayName => "Telegram Bot";
    public override string SystemPrompt => "You are a helpful AI assistant in a Telegram chat. Keep responses concise and well-formatted for mobile reading.";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        Activate(chatClient);
    }

    public async Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct)
    {
        try
        {
            if (update.MessageId > 0)
                await SetReaction(update.ChatId, update.MessageId, "\U0001F440", ct);

            if (IsStartCommand(update.Text))
            {
                await HandleStartCommand(update.ChatId, ct);
                return;
            }

            if (!string.IsNullOrEmpty(update.CallbackData))
            {
                await HandleCallback(update, ct);
                return;
            }

            if (!string.IsNullOrWhiteSpace(update.Text))
                await HandleTextMessage(update, ct);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Telegram API error handling update from chat {ChatId}", update.ChatId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error handling update from chat {ChatId}", update.ChatId);
        }
    }

    public async Task<TelegramSendResult> SendText(long chatId, string text, int? threadId, CancellationToken ct)
    {
        try
        {
            var message = await bot.SendMessageAsync(chatId, text,
                messageThreadId: threadId, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to send text to {ChatId}", chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task<TelegramSendResult> SendMarkdown(long chatId, string markdown, int? threadId, CancellationToken ct)
    {
        try
        {
            var message = await bot.SendMessageAsync(chatId, markdown,
                parseMode: FormatStyles.MarkdownV2,
                messageThreadId: threadId, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to send markdown to {ChatId}", chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task<TelegramSendResult> SendKeyboard(
        long chatId, string text, TelegramInlineButton[][] buttons, int? threadId, CancellationToken ct)
    {
        try
        {
            var keyboard = BuildInlineKeyboard(buttons);
            var message = await bot.SendMessageAsync(chatId, text,
                replyMarkup: keyboard,
                messageThreadId: threadId, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to send keyboard to {ChatId}", chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task<TelegramSendResult> EditMessage(
        long chatId, int messageId, string text, TelegramInlineButton[][]? buttons, CancellationToken ct)
    {
        try
        {
            InlineKeyboardMarkup? replyMarkup = buttons is not null ? BuildInlineKeyboard(buttons) : null;
            var message = await bot.EditMessageTextAsync(chatId, messageId, text,
                replyMarkup: replyMarkup, cancellationToken: ct);
            return TelegramSendResult.Ok(message.MessageId);
        }
        catch (BotRequestException ex)
        {
            logger.LogError(ex, "Failed to edit message {MessageId} in {ChatId}", messageId, chatId);
            return TelegramSendResult.Fail(ex.Message);
        }
    }

    public async Task SendTyping(long chatId, int? threadId, CancellationToken ct)
    {
        await bot.SendChatActionAsync(chatId, ChatActions.Typing,
            messageThreadId: threadId, cancellationToken: ct);
    }

    private async Task StreamResponseAsync(long chatId, int? threadId, string userMessage, CancellationToken ct)
    {
        var draftId = Random.Shared.Next(1, int.MaxValue);
        var accumulated = new StringBuilder();
        var throttle = TimeSpan.FromMilliseconds(400);
        var typingInterval = TimeSpan.FromSeconds(4);
        var lastDraftUpdate = DateTimeOffset.MinValue;
        var lastTypingUpdate = DateTimeOffset.UtcNow;

        try
        {
            await foreach (var token in SendAsync(userMessage, ct))
            {
                accumulated.Append(token);
                var now = DateTimeOffset.UtcNow;

                if (now - lastTypingUpdate >= typingInterval)
                {
                    try { await SendTyping(chatId, threadId, ct); }
                    catch (BotRequestException) { }
                    lastTypingUpdate = now;
                }

                if (now - lastDraftUpdate >= throttle)
                {
                    try
                    {
                        await bot.SendMessageDraftAsync(chatId, draftId, accumulated.ToString(),
                            messageThreadId: threadId, cancellationToken: ct);
                    }
                    catch (BotRequestException ex) when (ex.ErrorCode == 429)
                    {
                        logger.LogWarning("Rate limited during streaming, skipping draft update");
                    }
                    catch (BotRequestException ex)
                    {
                        logger.LogWarning(ex, "Draft update failed, continuing to accumulate");
                    }
                    lastDraftUpdate = now;
                }
            }

            var finalText = accumulated.Length > 0 ? accumulated.ToString() : "I couldn't generate a response.";
            await bot.SendMessageAsync(chatId, finalText,
                messageThreadId: threadId, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Streaming failed for chat {ChatId}", chatId);
            var errorText = accumulated.Length > 0
                ? accumulated + "\n\n(Response was interrupted)"
                : "Sorry, something went wrong. Please try again.";
            try { await SendText(chatId, errorText, threadId, ct); }
            catch (BotRequestException) { }
        }
    }

    public async Task SetReaction(long chatId, int messageId, string emoji, CancellationToken ct)
    {
        try
        {
            await bot.SetMessageReactionAsync(chatId, messageId,
                [new ReactionTypeEmoji(emoji)], cancellationToken: ct);
        }
        catch (BotRequestException ex)
        {
            logger.LogWarning(ex, "Failed to set reaction on message {MessageId}", messageId);
        }
    }

    public async Task PinMessage(long chatId, int messageId, int? threadId, CancellationToken ct)
    {
        await bot.PinChatMessageAsync(chatId, messageId, cancellationToken: ct);
    }

    public async Task<int> CreateTopic(long chatId, string name, CancellationToken ct)
    {
        var topic = await bot.CreateForumTopicAsync(chatId, name, cancellationToken: ct);
        return topic.MessageThreadId;
    }

    public async Task<TelegramTopicRegistry> EnsureTopics(long chatId, CancellationToken ct)
    {
        var existingJson = await GetStateValueAsync(TopicRegistryStateKey, ct);
        if (existingJson is not null)
        {
            var existing = JsonSerializer.Deserialize<TelegramTopicRegistry>(existingJson);
            if (existing is not null && existing.AssistantThreadId > 0)
                return existing;
        }

        try
        {
            var assistantThreadId = await CreateTopic(chatId, "Assistant", ct);
            var notificationsThreadId = await CreateTopic(chatId, "Notifications", ct);
            var settingsThreadId = await CreateTopic(chatId, "Settings", ct);

            var registry = new TelegramTopicRegistry
            {
                AssistantThreadId = assistantThreadId,
                NotificationsThreadId = notificationsThreadId,
                SettingsThreadId = settingsThreadId
            };

            await SetStateAsync(TopicRegistryStateKey, JsonSerializer.Serialize(registry), ct);
            return registry;
        }
        catch (BotRequestException ex) when (ex.Message.Contains("FORUM", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Chat {ChatId} does not have forum topics enabled", chatId);
            await SendText(chatId,
                "Please enable Topics in your group settings (via BotFather) and send /start again.", null, ct);
            return new TelegramTopicRegistry();
        }
    }

    public async Task SetWebhook(string url, string? secretToken, CancellationToken ct)
    {
        await bot.DeleteWebhookAsync(cancellationToken: ct);
        var args = new SetWebhookArgs(url);
        if (!string.IsNullOrWhiteSpace(secretToken))
            args.SecretToken = secretToken;
        await bot.SetWebhookAsync(args, ct);

        var info = await bot.GetWebhookInfoAsync(ct);
        logger.LogInformation("Webhook set to {Url}, verified: {Verified}", url, info.Url == url);
    }

    public async Task AnswerCallback(string callbackQueryId, string? text, CancellationToken ct)
    {
        await bot.AnswerCallbackQueryAsync(callbackQueryId, text, cancellationToken: ct);
    }

    private async Task HandleStartCommand(long chatId, CancellationToken ct)
    {
        await SendTyping(chatId, null, ct);
        var registry = await EnsureTopics(chatId, ct);

        if (registry.AssistantThreadId <= 0)
            return;

        var welcomeButtons = new TelegramInlineButton[][]
        {
            [new() { Text = "Chat with Assistant", CallbackData = "nav:assistant" }],
            [new() { Text = "View Notifications", CallbackData = "nav:notifications" }],
            [new() { Text = "Settings", CallbackData = "nav:settings" }]
        };

        await SendKeyboard(chatId,
            "Welcome to IAW Bot! Your topics are ready. Choose where to go:",
            welcomeButtons, null, ct);

        await SendText(chatId, "Send me a message in the Assistant topic to start chatting.",
            registry.AssistantThreadId, ct);
    }

    private async Task HandleTextMessage(TelegramBotUpdate update, CancellationToken ct)
    {
        var registryJson = await GetStateValueAsync(TopicRegistryStateKey, ct);
        if (registryJson is null)
        {
            await SendText(update.ChatId, "Send /start first to set up topics.", null, ct);
            return;
        }

        var registry = JsonSerializer.Deserialize<TelegramTopicRegistry>(registryJson);

        await SendTyping(update.ChatId, update.ThreadId, ct);

        if (update.ThreadId == registry?.SettingsThreadId)
        {
            await SendText(update.ChatId, "Settings: coming soon.", update.ThreadId, ct);
            return;
        }

        var router = GrainFactory.GetGrain<IAgentRouter>("router");
        var route = await router.RouteAsync(update.Text!, ct);

        var targetAgent = GrainFactory.GetGrain<IAgent>(route.AgentId);
        var agentMeta = await targetAgent.GetMetadataAsync(ct);

        logger.LogInformation("Routed message to {AgentId} (confidence: {Confidence}, escalated: {Escalated})",
            route.AgentId, route.Confidence, route.Escalated);

        await targetAgent.AddHistoryAsync("user", update.Text!, ct);
        await StreamResponseAsync(update.ChatId, update.ThreadId, update.Text!, ct);
    }

    private async Task HandleCallback(TelegramBotUpdate update, CancellationToken ct)
    {
        var registryJson = await GetStateValueAsync(TopicRegistryStateKey, ct);
        var registry = registryJson is not null
            ? JsonSerializer.Deserialize<TelegramTopicRegistry>(registryJson)
            : null;

        var message = update.CallbackData switch
        {
            "nav:assistant" when registry?.AssistantThreadId > 0
                => $"Head to the Assistant topic (thread #{registry.AssistantThreadId}) to chat.",
            "nav:notifications" when registry?.NotificationsThreadId > 0
                => "Check the Notifications topic for agent alerts.",
            "nav:settings" when registry?.SettingsThreadId > 0
                => "Visit the Settings topic to configure your bot.",
            _ => "Unknown action."
        };

        if (!string.IsNullOrEmpty(update.CallbackQueryId))
            await AnswerCallback(update.CallbackQueryId, message, ct);

        await SendText(update.ChatId, message, null, ct);
    }

    private static bool IsStartCommand(string? text)
        => string.Equals(text?.Trim(), StartCommand, StringComparison.OrdinalIgnoreCase);

    private static InlineKeyboardMarkup BuildInlineKeyboard(TelegramInlineButton[][] buttons)
    {
        var rows = buttons.Select(row =>
            row.Select(btn => new InlineKeyboardButton(btn.Text) { CallbackData = btn.CallbackData }).ToArray()
        ).ToArray();
        return new InlineKeyboardMarkup(rows);
    }
}
