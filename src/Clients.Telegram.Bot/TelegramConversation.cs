using IAW.Core;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using Telegram.BotAPI.UpdatingMessages;
using TelegramBot.Services;

namespace TelegramBot;

public sealed class TelegramConversation(
    [Memory("v2-messages")] IDurableList<AgentMessage> messages,
    [Memory("v2-memory")] IDurableDictionary<string, string> memory,
    [Memory("v2-events")] IDurableList<AgentEvent> events,
    [Memory("v2-subscriptions")] IDurableDictionary<string, List<string>> subscriptions,
    [Memory("v2-notifications")] IDurableList<NotificationRecord> notifications,
    [Memory("v2-tracking")] IDurableDictionary<string, string> tracking,
    [Llm<Claude45Haiku>] IChatClient chatClient,
    ITelegramBotClient bot,
    IHttpClientFactory httpClientFactory,
    IAudioConverter audioConverter,
    IVoiceTranscriptionService transcriptionService,
    ILogger<TelegramConversation> logger)
    : Agent(messages, memory, events, subscriptions, notifications, tracking),
      ITelegramConversation
{
    private const string TopicRegistryStateKey = "telegram:topic-registry";
    private const string MonitorRegistryStateKey = "telegram:monitor-registry";
    private const string SourceSubscriptionRegistryStateKey = "telegram:source-subscriptions";
    private const string MonitorProviderGrainId = "rss-source-provider";
    private const string StartCommand = "/start";
    private const string UserPreferenceChangedTopic = "user.preference.changed";
    private const int MinimumMonitorIntervalSeconds = 15;
    private const int DefaultMonitorMaxTicks = 100_000;

    private static readonly ActivitySource ActivitySource = new("TelegramBot");

    private static readonly Regex TrackingKeywordRegex = new(
        @"\b(track|tracking|traching|monitor|watch)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrackingIntervalRegex = new(
        @"\bevery\s+(?<value>\d+)\s*(?<unit>seconds?|secs?|s|minutes?|mins?|m)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PreferenceIntentRegex = new(
        @"\b(prefer|preference|remember|default|always|use|set)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, IGrainTimer> _monitorTimers = [];
    private readonly Dictionary<string, IGrainTimer> _sourceTimers = [];

    public override string DisplayName => "Telegram Bot";
    public override string SystemPrompt => "You are a helpful AI assistant in a Telegram chat. Keep responses concise and well-formatted for mobile reading.";

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        Activate(chatClient);

        var userAgent = GrainFactory.GetGrain<IAgent>("user");
        await userAgent.SubscribeAsync(UserPreferenceChangedTopic, this.GetPrimaryKeyString(), cancellationToken);

        await RestoreSourceTimersAsync(cancellationToken);
        await RestoreMonitorTimersAsync(cancellationToken);
    }

    public async Task HandleUpdate(TelegramBotUpdate update, CancellationToken ct)
    {
        var parentContext = TryCreateParentActivityContext(update);
        using var activity = parentContext is ActivityContext context
            ? ActivitySource.StartActivity("telegram.update.handle", ActivityKind.Consumer, context)
            : ActivitySource.StartActivity("telegram.update.handle", ActivityKind.Consumer);

        activity?.SetTag("telegram.chat_id", update.ChatId);
        activity?.SetTag("telegram.thread_id", update.ThreadId);
        activity?.SetTag("telegram.message_id", update.MessageId);
        activity?.SetTag("telegram.callback", !string.IsNullOrWhiteSpace(update.CallbackData));
        activity?.SetTag("telegram.has_voice", !string.IsNullOrWhiteSpace(update.VoiceFileId));
        activity?.SetTag("telegram.correlation_id", update.CorrelationId);

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = update.CorrelationId ?? activity?.TraceId.ToHexString(),
            ["ChatId"] = update.ChatId,
            ["ThreadId"] = update.ThreadId,
            ["MessageId"] = update.MessageId
        });

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

            if (!string.IsNullOrEmpty(update.VoiceFileId))
            {
                await HandleVoiceMessage(update, ct);
                return;
            }

            if (!string.IsNullOrWhiteSpace(update.Text))
                await HandleTextMessage(update, ct);
        }
        catch (BotRequestException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Telegram API error handling update from chat {ChatId}", update.ChatId);
        }
        catch (OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Unexpected error handling update from chat {ChatId}", update.ChatId);
            try
            {
                await SendText(update.ChatId, "Something went wrong processing your message. Please try again.",
                    update.ThreadId, ct);
            }
            catch (Exception sendEx)
            {
                logger.LogWarning(sendEx, "Failed to send error notification to chat {ChatId}", update.ChatId);
            }
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
        var existing = await LoadTopicRegistryAsync(ct);
        if (existing is not null && existing.AssistantThreadId > 0)
        {
            existing.TaskTopics ??= [];
            var updated = false;

            if (existing.TeamThreadId <= 0)
            {
                existing.TeamThreadId = await CreateTopic(chatId, "Team", ct);
                updated = true;
            }

            if (existing.NotificationsThreadId <= 0)
            {
                existing.NotificationsThreadId = await CreateTopic(chatId, "Notifications", ct);
                updated = true;
            }

            if (existing.SettingsThreadId <= 0)
            {
                existing.SettingsThreadId = await CreateTopic(chatId, "Settings", ct);
                updated = true;
            }

            if (updated)
                await SaveTopicRegistryAsync(existing, ct);

            return existing;
        }

        try
        {
            var assistantThreadId = await CreateTopic(chatId, "Assistant", ct);
            var teamThreadId = await CreateTopic(chatId, "Team", ct);
            var notificationsThreadId = await CreateTopic(chatId, "Notifications", ct);
            var settingsThreadId = await CreateTopic(chatId, "Settings", ct);

            var registry = new TelegramTopicRegistry
            {
                AssistantThreadId = assistantThreadId,
                TeamThreadId = teamThreadId,
                NotificationsThreadId = notificationsThreadId,
                SettingsThreadId = settingsThreadId
            };

            await SaveTopicRegistryAsync(registry, ct);
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

    private async Task PostToTeamTopicAsync(long chatId, int teamThreadId, string agentName, string message, CancellationToken ct)
    {
        var text = $"[{agentName}] {message}";
        try
        {
            await SendText(chatId, text, teamThreadId, ct);
        }
        catch (BotRequestException ex)
        {
            logger.LogWarning(ex, "Failed to post to Team topic");
        }
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

        await SendNotificationsOverviewAsync(chatId, registry, ct);
        await SendSettingsOverviewAsync(chatId, registry, null, null, ct);
    }

    private async Task HandleVoiceMessage(TelegramBotUpdate update, CancellationToken ct)
    {
        await SendTyping(update.ChatId, update.ThreadId, ct);

        string? wavPath = null;
        try
        {
            var file = await bot.GetFileAsync(update.VoiceFileId!, ct);
            var downloadUrl = $"{bot.Options.ServerAddress}/file/bot{bot.Options.BotToken}/{file.FilePath}";

            await using var oggStream = new MemoryStream();
            using var httpClient = httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync(downloadUrl, ct);
            response.EnsureSuccessStatusCode();
            await response.Content.CopyToAsync(oggStream, ct);
            oggStream.Position = 0;

            wavPath = await audioConverter.ConvertOggToWavAsync(oggStream, ct);

            var transcribedText = await transcriptionService.TranscribeAsync(wavPath, ct);

            if (string.IsNullOrWhiteSpace(transcribedText) || transcribedText.StartsWith('['))
            {
                var fallback = string.IsNullOrWhiteSpace(transcribedText)
                    ? "Could not transcribe the voice message."
                    : transcribedText;
                await SendText(update.ChatId, fallback, update.ThreadId, ct);
                return;
            }

            var preferences = await GetUserPreferencesAsync(update.ChatId, update.FromUserId, ct);
            var assistantInput = BuildAssistantInput(transcribedText, preferences);
            await StreamResponseAsync(update.ChatId, update.ThreadId, assistantInput, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Voice processing failed for chat {ChatId}", update.ChatId);
            try
            {
                await SendText(update.ChatId, "Sorry, I couldn't process your voice message. Please try again or send text instead.",
                    update.ThreadId, ct);
            }
            catch (Exception sendEx)
            {
                logger.LogWarning(sendEx, "Failed to send voice error notification to chat {ChatId}", update.ChatId);
            }
        }
        finally
        {
            if (wavPath is not null && System.IO.File.Exists(wavPath))
                System.IO.File.Delete(wavPath);
        }
    }

    private async Task HandleTextMessage(TelegramBotUpdate update, CancellationToken ct)
    {
        var registry = await EnsureTopics(update.ChatId, ct);
        if (registry.AssistantThreadId <= 0)
            return;

        await SendTyping(update.ChatId, update.ThreadId, ct);

        var preferenceChanges = await ApplyInferredPreferencesAsync(update, registry, ct);

        if (update.ThreadId == registry.SettingsThreadId)
        {
            await SendSettingsOverviewAsync(update.ChatId, registry, update.FromUserId, preferenceChanges, ct);
            return;
        }

        if (TryParseMonitoringIntent(update.Text!, out var monitorIntent))
        {
            await StartMonitorAsync(update, registry, monitorIntent, ct);
            return;
        }

        if (update.ThreadId == registry.NotificationsThreadId && IsNotificationsSummaryRequest(update.Text!))
        {
            await SendNotificationsOverviewAsync(update.ChatId, registry, ct);
            return;
        }

        var router = GrainFactory.GetGrain<IAgentRouter>("router");
        var route = await router.RouteAsync(update.Text!, ct);

        var targetAgent = GrainFactory.GetGrain<IAgent>(route.AgentId);
        var agentMeta = await targetAgent.GetMetadata(ct);

        logger.LogInformation("Routed message to {AgentId} (confidence: {Confidence}, escalated: {Escalated})",
            route.AgentId, route.Confidence, route.Escalated);

        if (route.Escalated && registry.TeamThreadId > 0)
        {
            await PostToTeamTopicAsync(update.ChatId, registry.TeamThreadId,
                "Router", $"Delegated to {agentMeta.DisplayName}: {update.Text}", ct);
        }

        var preferences = await GetUserPreferencesAsync(update.ChatId, update.FromUserId, ct);
        var assistantInput = BuildAssistantInput(update.Text!, preferences);

        await targetAgent.AddHistoryAsync("user", update.Text!, ct);
        await StreamResponseAsync(update.ChatId, update.ThreadId, assistantInput, ct);
    }

    private async Task HandleCallback(TelegramBotUpdate update, CancellationToken ct)
    {
        var registry = await LoadTopicRegistryAsync(ct);

        if (string.IsNullOrWhiteSpace(update.CallbackData))
            return;

        if (TryParseMonitorCallback(update.CallbackData, out var action, out var monitorId))
        {
            var monitorMessage = await HandleMonitorCallbackAsync(update.ChatId, action, monitorId, ct);
            if (!string.IsNullOrEmpty(update.CallbackQueryId))
                await AnswerCallback(update.CallbackQueryId, monitorMessage, ct);
            return;
        }

        var message = update.CallbackData switch
        {
            "nav:assistant" when registry?.AssistantThreadId > 0
                => "Assistant topic opened.",
            "nav:notifications" when registry?.NotificationsThreadId > 0
                => "Notifications topic opened.",
            "nav:settings" when registry?.SettingsThreadId > 0
                => "Settings topic opened.",
            _ => "Unknown action."
        };

        if (!string.IsNullOrEmpty(update.CallbackQueryId))
            await AnswerCallback(update.CallbackQueryId, message, ct);

        switch (update.CallbackData)
        {
            case "nav:assistant" when registry?.AssistantThreadId > 0:
                await SendText(update.ChatId, "Assistant is ready for your next message.", registry.AssistantThreadId, ct);
                break;
            case "nav:notifications" when registry?.NotificationsThreadId > 0:
                await SendNotificationsOverviewAsync(update.ChatId, registry, ct);
                break;
            case "nav:settings" when registry?.SettingsThreadId > 0:
                await SendSettingsOverviewAsync(update.ChatId, registry, update.FromUserId, null, ct);
                break;
            default:
                await SendText(update.ChatId, "Unknown action.", null, ct);
                break;
        }
    }

    private async Task<Dictionary<string, string>> ApplyInferredPreferencesAsync(
        TelegramBotUpdate update,
        TelegramTopicRegistry topicRegistry,
        CancellationToken ct)
    {
        var extracted = ExtractPreferenceUpdates(update.Text);
        if (extracted.Count == 0)
            return [];

        var stateKey = GetPreferenceStateKey(update.ChatId, update.FromUserId);
        var userAgent = GrainFactory.GetGrain<IAgent>("user");
        var existing = await LoadPreferencesForStateKeyAsync(userAgent, stateKey, ct);

        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in extracted)
        {
            if (!existing.TryGetValue(key, out var current) ||
                !string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
            {
                existing[key] = value;
                changes[key] = value;
            }
        }

        if (changes.Count == 0)
            return [];

        await userAgent.SetStateAsync(stateKey, JsonSerializer.Serialize(existing), ct);

        var payload = JsonSerializer.Serialize(new PreferenceChangeEvent
        {
            ChatId = update.ChatId,
            UserId = update.FromUserId,
            Changes = changes,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        await userAgent.NotifyAsync(UserPreferenceChangedTopic, payload, ct);

        if (topicRegistry.SettingsThreadId > 0)
        {
            var summary = "Updated preferences:\n" + string.Join('\n',
                changes.Select(kvp => $"- {kvp.Key} = {kvp.Value}"));

            await SendText(update.ChatId, summary, topicRegistry.SettingsThreadId, ct);
        }

        return changes;
    }

    private async Task<Dictionary<string, string>> GetUserPreferencesAsync(long chatId, long? fromUserId, CancellationToken ct)
    {
        var userAgent = GrainFactory.GetGrain<IAgent>("user");
        var stateKey = GetPreferenceStateKey(chatId, fromUserId);
        return await LoadPreferencesForStateKeyAsync(userAgent, stateKey, ct);
    }

    private static async Task<Dictionary<string, string>> LoadPreferencesForStateKeyAsync(IAgent userAgent, string stateKey, CancellationToken ct)
    {
        var existingJson = await userAgent.GetStateValueAsync(stateKey, ct);
        if (string.IsNullOrWhiteSpace(existingJson))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(existingJson);
            return existing is not null
                ? new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task SendSettingsOverviewAsync(
        long chatId,
        TelegramTopicRegistry topicRegistry,
        long? fromUserId,
        IReadOnlyDictionary<string, string>? recentChanges,
        CancellationToken ct)
    {
        if (topicRegistry.SettingsThreadId <= 0)
            return;

        var preferences = await GetUserPreferencesAsync(chatId, fromUserId, ct);
        var monitorRegistry = await LoadMonitorRegistryAsync(ct);

        var running = monitorRegistry.Monitors.Values.Count(m => m.Lifecycle == MonitorLifecycle.Running);
        var paused = monitorRegistry.Monitors.Values.Count(m => m.Lifecycle == MonitorLifecycle.Paused);
        var stopped = monitorRegistry.Monitors.Values.Count(m => m.Lifecycle == MonitorLifecycle.Stopped);

        var text = new StringBuilder();
        text.AppendLine("Settings");
        text.AppendLine();

        if (preferences.Count == 0)
        {
            text.AppendLine("Preferences: not configured.");
        }
        else
        {
            text.AppendLine("Saved preferences:");
            foreach (var (key, value) in preferences.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                text.AppendLine($"- {key}: {value}");
        }

        if (recentChanges is { Count: > 0 })
        {
            text.AppendLine();
            text.AppendLine("Latest updates:");
            foreach (var (key, value) in recentChanges)
                text.AppendLine($"- {key}: {value}");
        }

        text.AppendLine();
        text.AppendLine($"Monitors: {running} running, {paused} paused, {stopped} stopped.");
        text.AppendLine("Tip: say \"I prefer weather in Celsius\" to persist defaults.");
        text.AppendLine("Tip: say \"start tracking new posts from elonmusk every 15 seconds\" to create a monitor topic.");
        text.AppendLine("Source support: RSS/Atom URLs, X handles (@name), Reddit subs (r/name).");

        await SendText(chatId, text.ToString().TrimEnd(), topicRegistry.SettingsThreadId, ct);
    }

    private async Task SendNotificationsOverviewAsync(long chatId, TelegramTopicRegistry topicRegistry, CancellationToken ct)
    {
        if (topicRegistry.NotificationsThreadId <= 0)
            return;

        var monitorRegistry = await LoadMonitorRegistryAsync(ct);
        var active = monitorRegistry.Monitors.Values
            .Where(m => m.Lifecycle is MonitorLifecycle.Running or MonitorLifecycle.Paused)
            .OrderByDescending(m => m.LastCheckedAtUtc ?? m.CreatedAtUtc)
            .Take(5)
            .ToList();

        var text = new StringBuilder();
        text.AppendLine("Notifications Overview");
        text.AppendLine($"Active monitors: {active.Count}");

        if (active.Count == 0)
        {
            text.AppendLine("No active monitors.");
        }
        else
        {
            text.AppendLine();
            foreach (var monitor in active)
            {
                var statusLabel = monitor.Lifecycle == MonitorLifecycle.Running ? "running" : "paused";
                text.AppendLine($"- {monitor.Source} ({statusLabel}, every {monitor.IntervalSeconds}s)");
                text.AppendLine($"  Last check: {FormatRelativeTime(monitor.LastCheckedAtUtc)}");
                text.AppendLine($"  Last signal time: {FormatRelativeTime(monitor.LastSignalAtUtc)}");
                text.AppendLine($"  Last signal: {monitor.LastStatusText}");
            }
        }

        text.AppendLine();
        text.AppendLine("Use monitor cards to pause/resume/unsubscribe.");

        await SendText(chatId, text.ToString().TrimEnd(), topicRegistry.NotificationsThreadId, ct);
    }

    private async Task StartMonitorAsync(TelegramBotUpdate update, TelegramTopicRegistry topicRegistry, MonitorIntent monitorIntent, CancellationToken ct)
    {
        var monitorRegistry = await LoadMonitorRegistryAsync(ct);

        var existing = monitorRegistry.Monitors.Values.FirstOrDefault(m =>
            !string.Equals(m.MonitorId, string.Empty, StringComparison.Ordinal) &&
            string.Equals(m.Source, monitorIntent.Source, StringComparison.OrdinalIgnoreCase) &&
            m.Lifecycle != MonitorLifecycle.Stopped);

        if (existing is not null)
        {
            var existingStatus = existing.Lifecycle == MonitorLifecycle.Running ? "running" : "paused";
            await SendText(update.ChatId,
                $"Already monitoring \"{existing.Source}\" ({existingStatus}) in thread #{existing.ThreadId}.",
                update.ThreadId ?? topicRegistry.AssistantThreadId,
                ct);
            return;
        }

        var monitorId = Guid.NewGuid().ToString("N")[..8];
        var agentId = $"monitor:{this.GetPrimaryKeyString()}:{monitorId}";

        var monitor = new TelegramMonitorState
        {
            MonitorId = monitorId,
            AgentId = agentId,
            ProviderId = MonitorProviderGrainId,
            SourceKey = BuildSourceKey(MonitorProviderGrainId, monitorIntent.Source),
            ChatId = update.ChatId,
            ThreadId = 0,
            Source = monitorIntent.Source,
            RawQuery = monitorIntent.RawQuery,
            IntervalSeconds = monitorIntent.IntervalSeconds,
            RequestedIntervalSeconds = monitorIntent.RequestedIntervalSeconds,
            MaxTicks = DefaultMonitorMaxTicks,
            Lifecycle = MonitorLifecycle.Running,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            LastStatusText = "No new posts detected yet."
        };

        var baseline = await PollMonitorSourceAsync(monitor, emitInitialItems: false, ct);
        if (!baseline.Success && baseline.Status.StartsWith("Unsupported source", StringComparison.OrdinalIgnoreCase))
        {
            await SendText(update.ChatId,
                baseline.Status,
                update.ThreadId ?? topicRegistry.AssistantThreadId,
                ct);
            return;
        }

        if (baseline.Success)
        {
            monitor.SourceCursor = baseline.NextCursor;
            monitor.LastCheckedAtUtc = baseline.CheckedAtUtc;
            monitor.LastStatusText = baseline.Status;
        }
        else
        {
            monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            monitor.LastStatusText = baseline.Status;
        }

        var threadId = await EnsureTaskTopicAsync(update.ChatId, topicRegistry, monitorId, monitorIntent.Source, ct);
        monitor.ThreadId = threadId;

        var monitorAgent = GrainFactory.GetGrain<IAgent>(monitor.AgentId);
        await monitorAgent.StartTrackingAsync(TimeSpan.FromSeconds(monitor.IntervalSeconds), monitor.MaxTicks, ct);

        monitorRegistry.Monitors[monitor.MonitorId] = monitor;
        await SaveMonitorRegistryAsync(monitorRegistry, ct);
        await SubscribeMonitorToSourceAsync(monitor, baseline, ct);

        var status = await monitorAgent.GetTrackingStatusAsync(ct);
        var cardText = BuildMonitorCardText(monitor, status);
        var cardResult = await SendKeyboard(update.ChatId, cardText, BuildMonitorButtons(monitor), threadId, ct);

        if (cardResult.Success)
        {
            monitor.CardMessageId = cardResult.MessageId;
            monitorRegistry.Monitors[monitor.MonitorId] = monitor;
            await SaveMonitorRegistryAsync(monitorRegistry, ct);
        }

        StartOrReplaceMonitorTimer(monitor.MonitorId, TimeSpan.FromSeconds(monitor.IntervalSeconds));

        var intervalWarning = monitor.RequestedIntervalSeconds != monitor.IntervalSeconds
            ? $" Requested {monitor.RequestedIntervalSeconds}s was adjusted to {monitor.IntervalSeconds}s to avoid flooding."
            : string.Empty;

        await SendText(update.ChatId,
            $"Started tracking \"{monitor.Source}\" every {monitor.IntervalSeconds}s.{intervalWarning} " +
            $"Manage it in task thread #{threadId}.",
            update.ThreadId ?? topicRegistry.AssistantThreadId,
            ct);

        if (topicRegistry.NotificationsThreadId > 0)
        {
            await SendText(update.ChatId,
                $"New monitor started: {monitor.Source} (every {monitor.IntervalSeconds}s).",
                topicRegistry.NotificationsThreadId,
                ct);
        }
    }

    private async Task<string> HandleMonitorCallbackAsync(
        long chatId,
        string action,
        string monitorId,
        CancellationToken ct)
    {
        var monitorRegistry = await LoadMonitorRegistryAsync(ct);
        if (!monitorRegistry.Monitors.TryGetValue(monitorId, out var monitor))
            return "Monitor not found.";

        var monitorAgent = GrainFactory.GetGrain<IAgent>(monitor.AgentId);

        switch (action)
        {
            case "pause":
            {
                if (monitor.Lifecycle == MonitorLifecycle.Paused)
                    return "Monitor is already paused.";

                if (monitor.Lifecycle == MonitorLifecycle.Stopped)
                    return "Monitor is stopped. Use Resume to start it again.";

                var status = await monitorAgent.GetTrackingStatusAsync(ct);
                monitor.TotalTickCount = Math.Max(monitor.TotalTickCount, monitor.TickOffset + status.TickCount);
                monitor.TickOffset = monitor.TotalTickCount;

                await monitorAgent.StopTrackingAsync(ct);
                monitor.Lifecycle = MonitorLifecycle.Paused;
                monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
                monitor.LastStatusText = "Paused by user.";
                StopMonitorTimer(monitorId);
                await UnsubscribeMonitorFromSourceAsync(monitor, ct);
                break;
            }

            case "resume":
            {
                if (monitor.Lifecycle == MonitorLifecycle.Running)
                    return "Monitor is already running.";

                var remainingTicks = Math.Max(monitor.MaxTicks - monitor.TickOffset, 1);
                await monitorAgent.StartTrackingAsync(
                    TimeSpan.FromSeconds(monitor.IntervalSeconds),
                    remainingTicks,
                    ct);

                monitor.Lifecycle = MonitorLifecycle.Running;
                monitor.CompletionNotified = false;
                monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
                monitor.LastStatusText = "Resumed. No new posts detected yet.";
                StartOrReplaceMonitorTimer(monitorId, TimeSpan.FromSeconds(monitor.IntervalSeconds));
                await SubscribeMonitorToSourceAsync(monitor, baseline: null, ct);
                break;
            }

            case "stop":
            {
                await monitorAgent.StopTrackingAsync(ct);
                monitor.Lifecycle = MonitorLifecycle.Stopped;
                monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
                monitor.LastStatusText = "Unsubscribed by user.";
                monitor.CompletionNotified = true;
                StopMonitorTimer(monitorId);
                await UnsubscribeMonitorFromSourceAsync(monitor, ct);
                break;
            }

            case "refresh":
                await RefreshSourceSubscriptionAsync(monitor.SourceKey, ct);
                return "Refreshed.";

            default:
                return "Unknown monitor action.";
        }

        monitorRegistry.Monitors[monitor.MonitorId] = monitor;
        await SaveMonitorRegistryAsync(monitorRegistry, ct);
        await RefreshMonitorCardAsync(monitorId, ct, force: true);

        if (action == "stop")
        {
            var topicRegistry = await LoadTopicRegistryAsync(ct);
            if (topicRegistry?.NotificationsThreadId > 0)
            {
                await SendText(chatId,
                    $"Unsubscribed from monitor: {monitor.Source}.",
                    topicRegistry.NotificationsThreadId,
                    ct);
            }

            return "Unsubscribed.";
        }

        return action switch
        {
            "pause" => "Paused.",
            "resume" => "Resumed.",
            _ => "Done."
        };
    }

    private async Task<MonitorPollResult> PollMonitorSourceAsync(
        TelegramMonitorState monitor,
        bool emitInitialItems,
        CancellationToken ct)
    {
        var providerId = string.IsNullOrWhiteSpace(monitor.ProviderId)
            ? MonitorProviderGrainId
            : monitor.ProviderId;

        var provider = GrainFactory.GetGrain<IMonitorSourceProvider>(providerId);
        var result = await provider.PollAsync(new MonitorPollRequest
        {
            Source = monitor.Source,
            RawQuery = monitor.RawQuery,
            Cursor = monitor.SourceCursor,
            EmitInitialItems = emitInitialItems,
            MaxItems = 5
        }, ct);

        if (string.IsNullOrWhiteSpace(result.ProviderId))
            result.ProviderId = providerId;

        return result;
    }

    private async Task<MonitorPollResult> PollSourceSubscriptionAsync(
        TelegramSourceSubscriptionState sourceSubscription,
        bool emitInitialItems,
        CancellationToken ct)
    {
        var providerId = string.IsNullOrWhiteSpace(sourceSubscription.ProviderId)
            ? MonitorProviderGrainId
            : sourceSubscription.ProviderId;

        var provider = GrainFactory.GetGrain<IMonitorSourceProvider>(providerId);
        var result = await provider.PollAsync(new MonitorPollRequest
        {
            Source = sourceSubscription.Source,
            RawQuery = sourceSubscription.RawQuery,
            Cursor = sourceSubscription.Cursor,
            EmitInitialItems = emitInitialItems,
            MaxItems = 5
        }, ct);

        if (string.IsNullOrWhiteSpace(result.ProviderId))
            result.ProviderId = providerId;

        return result;
    }

    private async Task SubscribeMonitorToSourceAsync(
        TelegramMonitorState monitor,
        MonitorPollResult? baseline,
        CancellationToken ct)
    {
        var sourceRegistry = await LoadSourceSubscriptionRegistryAsync(ct);
        var monitorRegistry = await LoadMonitorRegistryAsync(ct);

        monitor.SourceKey = string.IsNullOrWhiteSpace(monitor.SourceKey)
            ? BuildSourceKey(monitor.ProviderId, monitor.Source)
            : monitor.SourceKey;

        if (!sourceRegistry.Sources.TryGetValue(monitor.SourceKey, out var sourceSubscription))
        {
            sourceSubscription = new TelegramSourceSubscriptionState
            {
                SourceKey = monitor.SourceKey,
                ProviderId = monitor.ProviderId,
                Source = monitor.Source,
                RawQuery = monitor.RawQuery,
                Cursor = baseline?.NextCursor ?? monitor.SourceCursor,
                LastCheckedAtUtc = baseline?.CheckedAtUtc ?? monitor.LastCheckedAtUtc,
                LastStatusText = baseline?.Status ?? monitor.LastStatusText
            };

            sourceRegistry.Sources[monitor.SourceKey] = sourceSubscription;
        }

        if (!sourceSubscription.MonitorIds.Contains(monitor.MonitorId, StringComparer.Ordinal))
            sourceSubscription.MonitorIds.Add(monitor.MonitorId);

        sourceSubscription.ProviderId = monitor.ProviderId;
        sourceSubscription.Source = monitor.Source;
        sourceSubscription.RawQuery = monitor.RawQuery;

        if (!string.IsNullOrWhiteSpace(monitor.SourceCursor))
            sourceSubscription.Cursor = monitor.SourceCursor;

        if (baseline is not null)
        {
            sourceSubscription.Cursor = baseline.NextCursor ?? sourceSubscription.Cursor;
            sourceSubscription.LastCheckedAtUtc = baseline.CheckedAtUtc;
            sourceSubscription.LastStatusText = baseline.Status;
        }

        await SaveSourceSubscriptionRegistryAsync(sourceRegistry, ct);
        await ReconcileSourceTimerAsync(sourceSubscription.SourceKey, sourceSubscription, monitorRegistry, ct);
    }

    private async Task UnsubscribeMonitorFromSourceAsync(TelegramMonitorState monitor, CancellationToken ct)
    {
        var sourceKey = string.IsNullOrWhiteSpace(monitor.SourceKey)
            ? BuildSourceKey(monitor.ProviderId, monitor.Source)
            : monitor.SourceKey;

        var sourceRegistry = await LoadSourceSubscriptionRegistryAsync(ct);
        if (!sourceRegistry.Sources.TryGetValue(sourceKey, out var sourceSubscription))
            return;

        sourceSubscription.MonitorIds = sourceSubscription.MonitorIds
            .Where(id => !string.Equals(id, monitor.MonitorId, StringComparison.Ordinal))
            .ToList();

        if (sourceSubscription.MonitorIds.Count == 0)
        {
            sourceRegistry.Sources.Remove(sourceKey);
            await SaveSourceSubscriptionRegistryAsync(sourceRegistry, ct);
            StopSourceTimer(sourceKey);
            return;
        }

        sourceRegistry.Sources[sourceKey] = sourceSubscription;
        await SaveSourceSubscriptionRegistryAsync(sourceRegistry, ct);

        var monitorRegistry = await LoadMonitorRegistryAsync(ct);
        await ReconcileSourceTimerAsync(sourceKey, sourceSubscription, monitorRegistry, ct);
    }

    private async Task RestoreSourceTimersAsync(CancellationToken ct)
    {
        var sourceRegistry = await LoadSourceSubscriptionRegistryAsync(ct);
        var monitorRegistry = await LoadMonitorRegistryAsync(ct);

        foreach (var (sourceKey, sourceSubscription) in sourceRegistry.Sources)
            await ReconcileSourceTimerAsync(sourceKey, sourceSubscription, monitorRegistry, ct);
    }

    private async Task RefreshSourceSubscriptionAsync(string sourceKey, CancellationToken ct)
    {
        var sourceRegistry = await LoadSourceSubscriptionRegistryAsync(ct);
        if (!sourceRegistry.Sources.TryGetValue(sourceKey, out var sourceSubscription))
        {
            StopSourceTimer(sourceKey);
            return;
        }

        var monitorRegistry = await LoadMonitorRegistryAsync(ct);

        var runningMonitors = sourceSubscription.MonitorIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => monitorRegistry.Monitors.TryGetValue(id, out var monitor) ? monitor : null)
            .Where(monitor => monitor is not null && monitor.Lifecycle == MonitorLifecycle.Running)
            .Cast<TelegramMonitorState>()
            .ToList();

        if (runningMonitors.Count == 0)
        {
            sourceSubscription.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            sourceSubscription.LastStatusText = "No active subscribers.";
            sourceRegistry.Sources[sourceKey] = sourceSubscription;
            await SaveSourceSubscriptionRegistryAsync(sourceRegistry, ct);
            StopSourceTimer(sourceKey);
            return;
        }

        var poll = await PollSourceSubscriptionAsync(sourceSubscription, emitInitialItems: false, ct);
        sourceSubscription.LastCheckedAtUtc = poll.CheckedAtUtc;
        sourceSubscription.LastStatusText = poll.Status;

        if (poll.Success)
            sourceSubscription.Cursor = poll.NextCursor ?? sourceSubscription.Cursor;

        var changedMonitors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var monitor in runningMonitors)
        {
            monitor.LastCheckedAtUtc = poll.CheckedAtUtc;
            monitor.LastStatusText = poll.Status;

            if (poll.Success)
                monitor.SourceCursor = poll.NextCursor ?? monitor.SourceCursor;

            if (poll.NewItems.Count > 0)
            {
                monitor.LastSignalAtUtc = poll.CheckedAtUtc;
                await PublishMonitorSignalsAsync(monitor, poll.NewItems, ct);
            }

            monitorRegistry.Monitors[monitor.MonitorId] = monitor;
            changedMonitors.Add(monitor.MonitorId);
        }

        sourceRegistry.Sources[sourceKey] = sourceSubscription;
        await SaveSourceSubscriptionRegistryAsync(sourceRegistry, ct);

        if (changedMonitors.Count > 0)
            await SaveMonitorRegistryAsync(monitorRegistry, ct);

        foreach (var monitorId in changedMonitors)
            await RefreshMonitorCardAsync(monitorId, ct, force: false);

        await ReconcileSourceTimerAsync(sourceKey, sourceSubscription, monitorRegistry, ct);
    }

    private async Task ReconcileSourceTimerAsync(
        string sourceKey,
        TelegramSourceSubscriptionState sourceSubscription,
        TelegramMonitorRegistry monitorRegistry,
        CancellationToken ct)
    {
        var runningIntervals = sourceSubscription.MonitorIds
            .Distinct(StringComparer.Ordinal)
            .Select(id => monitorRegistry.Monitors.TryGetValue(id, out var monitor) ? monitor : null)
            .Where(monitor => monitor is not null && monitor.Lifecycle == MonitorLifecycle.Running)
            .Select(monitor => monitor!.IntervalSeconds)
            .Where(seconds => seconds >= MinimumMonitorIntervalSeconds)
            .ToList();

        if (runningIntervals.Count == 0)
        {
            StopSourceTimer(sourceKey);
            return;
        }

        var minInterval = runningIntervals.Min();
        StartOrReplaceSourceTimer(sourceKey, TimeSpan.FromSeconds(minInterval));

        if (sourceSubscription.LastCheckedAtUtc is null)
            await RefreshSourceSubscriptionAsync(sourceKey, ct);
    }

    private void StartOrReplaceSourceTimer(string sourceKey, TimeSpan period)
    {
        StopSourceTimer(sourceKey);
        _sourceTimers[sourceKey] = this.RegisterGrainTimer(
            () => RefreshSourceSubscriptionAsync(sourceKey, CancellationToken.None),
            period,
            period);
    }

    private void StopSourceTimer(string sourceKey)
    {
        if (_sourceTimers.Remove(sourceKey, out var timer))
            timer.Dispose();
    }

    private async Task PublishMonitorSignalsAsync(
        TelegramMonitorState monitor,
        IReadOnlyList<MonitorFeedItem> newItems,
        CancellationToken ct)
    {
        if (newItems.Count == 0)
            return;

        var cappedItems = newItems.Take(3).ToList();
        var detail = new StringBuilder();
        detail.AppendLine($"New updates from {monitor.Source}");
        detail.AppendLine($"Detected: {newItems.Count} post(s)");
        detail.AppendLine();

        for (var i = 0; i < cappedItems.Count; i++)
        {
            var item = cappedItems[i];
            detail.AppendLine($"{i + 1}. {item.Title}");
            if (item.PublishedAtUtc is { } publishedAtUtc)
                detail.AppendLine($"   {publishedAtUtc:yyyy-MM-dd HH:mm} UTC");
            if (!string.IsNullOrWhiteSpace(item.Url))
                detail.AppendLine($"   {item.Url}");
        }

        if (newItems.Count > cappedItems.Count)
            detail.AppendLine($"...and {newItems.Count - cappedItems.Count} more.");

        await SendKeyboard(
            monitor.ChatId,
            detail.ToString().TrimEnd(),
            BuildMonitorSignalButtons(monitor.MonitorId),
            monitor.ThreadId,
            ct);

        var topicRegistry = await LoadTopicRegistryAsync(ct);
        if (topicRegistry?.NotificationsThreadId > 0)
        {
            await SendText(
                monitor.ChatId,
                $"New posts from {monitor.Source}: {newItems.Count}. Check task thread #{monitor.ThreadId}.",
                topicRegistry.NotificationsThreadId,
                ct);
        }
    }

    private async Task RefreshMonitorCardAsync(string monitorId, CancellationToken ct, bool force = false)
    {
        var monitorRegistry = await LoadMonitorRegistryAsync(ct);
        if (!monitorRegistry.Monitors.TryGetValue(monitorId, out var monitor))
        {
            StopMonitorTimer(monitorId);
            return;
        }

        AgentTrackingStatus status;
        try
        {
            status = await GrainFactory.GetGrain<IAgent>(monitor.AgentId).GetTrackingStatusAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get tracking status for monitor {MonitorId}", monitorId);
            return;
        }

        var stateChanged = false;
        var totalTickCount = monitor.TickOffset + status.TickCount;
        if (totalTickCount > monitor.TotalTickCount)
        {
            monitor.TotalTickCount = totalTickCount;
            monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            stateChanged = true;
        }
        else if (force && monitor.Lifecycle is MonitorLifecycle.Running or MonitorLifecycle.Paused)
        {
            monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            stateChanged = true;
        }

        if (monitor.Lifecycle == MonitorLifecycle.Running && !status.IsTracking)
        {
            monitor.Lifecycle = MonitorLifecycle.Stopped;
            monitor.TotalTickCount = Math.Max(monitor.TotalTickCount, monitor.TickOffset + status.TickCount);
            monitor.TickOffset = monitor.TotalTickCount;
            monitor.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            monitor.LastStatusText = monitor.TotalTickCount >= monitor.MaxTicks
                ? "Monitoring window completed."
                : "Stopped.";
            stateChanged = true;
            StopMonitorTimer(monitorId);
            await UnsubscribeMonitorFromSourceAsync(monitor, ct);
        }

        var cardText = BuildMonitorCardText(monitor, status);
        var buttons = BuildMonitorButtons(monitor);

        if (monitor.CardMessageId > 0)
        {
            var editResult = await EditMessage(monitor.ChatId, monitor.CardMessageId, cardText, buttons, ct);
            if (!editResult.Success)
            {
                var replacement = await SendKeyboard(monitor.ChatId, cardText, buttons, monitor.ThreadId, ct);
                if (replacement.Success)
                {
                    monitor.CardMessageId = replacement.MessageId;
                    stateChanged = true;
                }
            }
        }
        else
        {
            var sendResult = await SendKeyboard(monitor.ChatId, cardText, buttons, monitor.ThreadId, ct);
            if (sendResult.Success)
            {
                monitor.CardMessageId = sendResult.MessageId;
                stateChanged = true;
            }
        }

        if (monitor.Lifecycle == MonitorLifecycle.Stopped && !monitor.CompletionNotified)
        {
            var topicRegistry = await LoadTopicRegistryAsync(ct);
            if (topicRegistry?.NotificationsThreadId > 0)
            {
                await SendText(monitor.ChatId,
                    $"Monitor completed: {monitor.Source}.",
                    topicRegistry.NotificationsThreadId,
                    ct);
            }

            monitor.CompletionNotified = true;
            stateChanged = true;
        }

        if (stateChanged)
        {
            monitorRegistry.Monitors[monitorId] = monitor;
            await SaveMonitorRegistryAsync(monitorRegistry, ct);
        }
    }

    private async Task RestoreMonitorTimersAsync(CancellationToken ct)
    {
        var monitorRegistry = await LoadMonitorRegistryAsync(ct);

        foreach (var monitor in monitorRegistry.Monitors.Values)
        {
            if (monitor.Lifecycle != MonitorLifecycle.Running)
                continue;

            StartOrReplaceMonitorTimer(monitor.MonitorId, TimeSpan.FromSeconds(monitor.IntervalSeconds));
            await RefreshMonitorCardAsync(monitor.MonitorId, ct, force: false);
        }
    }

    private void StartOrReplaceMonitorTimer(string monitorId, TimeSpan period)
    {
        StopMonitorTimer(monitorId);
        _monitorTimers[monitorId] = this.RegisterGrainTimer(
            () => RefreshMonitorCardAsync(monitorId, CancellationToken.None),
            period,
            period);
    }

    private void StopMonitorTimer(string monitorId)
    {
        if (_monitorTimers.Remove(monitorId, out var timer))
            timer.Dispose();
    }

    private async Task<TelegramMonitorRegistry> LoadMonitorRegistryAsync(CancellationToken ct)
    {
        var json = await GetStateValueAsync(MonitorRegistryStateKey, ct);
        if (string.IsNullOrWhiteSpace(json))
            return new TelegramMonitorRegistry();

        try
        {
            var registry = JsonSerializer.Deserialize<TelegramMonitorRegistry>(json);
            if (registry is null)
                return new TelegramMonitorRegistry();

            registry.Monitors ??= [];
            foreach (var monitor in registry.Monitors.Values)
            {
                if (string.IsNullOrWhiteSpace(monitor.ProviderId))
                    monitor.ProviderId = MonitorProviderGrainId;

                if (string.IsNullOrWhiteSpace(monitor.SourceKey))
                    monitor.SourceKey = BuildSourceKey(monitor.ProviderId, monitor.Source);

                if (monitor.IntervalSeconds < MinimumMonitorIntervalSeconds)
                    monitor.IntervalSeconds = MinimumMonitorIntervalSeconds;
            }

            return registry;
        }
        catch (JsonException)
        {
            return new TelegramMonitorRegistry();
        }
    }

    private Task SaveMonitorRegistryAsync(TelegramMonitorRegistry registry, CancellationToken ct)
        => SetStateAsync(MonitorRegistryStateKey, JsonSerializer.Serialize(registry), ct);

    private async Task<TelegramSourceSubscriptionRegistry> LoadSourceSubscriptionRegistryAsync(CancellationToken ct)
    {
        var json = await GetStateValueAsync(SourceSubscriptionRegistryStateKey, ct);
        if (string.IsNullOrWhiteSpace(json))
            return new TelegramSourceSubscriptionRegistry();

        try
        {
            var registry = JsonSerializer.Deserialize<TelegramSourceSubscriptionRegistry>(json);
            if (registry is null)
                return new TelegramSourceSubscriptionRegistry();

            registry.Sources ??= [];
            foreach (var source in registry.Sources.Values)
            {
                if (string.IsNullOrWhiteSpace(source.ProviderId))
                    source.ProviderId = MonitorProviderGrainId;

                source.MonitorIds ??= [];
            }

            return registry;
        }
        catch (JsonException)
        {
            return new TelegramSourceSubscriptionRegistry();
        }
    }

    private Task SaveSourceSubscriptionRegistryAsync(TelegramSourceSubscriptionRegistry registry, CancellationToken ct)
        => SetStateAsync(SourceSubscriptionRegistryStateKey, JsonSerializer.Serialize(registry), ct);

    private async Task<TelegramTopicRegistry?> LoadTopicRegistryAsync(CancellationToken ct)
    {
        var json = await GetStateValueAsync(TopicRegistryStateKey, ct);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var registry = JsonSerializer.Deserialize<TelegramTopicRegistry>(json);
            if (registry is null)
                return null;

            registry.TaskTopics ??= [];
            return registry;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task SaveTopicRegistryAsync(TelegramTopicRegistry registry, CancellationToken ct)
    {
        registry.TaskTopics ??= [];
        return SetStateAsync(TopicRegistryStateKey, JsonSerializer.Serialize(registry), ct);
    }

    private async Task<int> EnsureTaskTopicAsync(
        long chatId,
        TelegramTopicRegistry topicRegistry,
        string monitorId,
        string source,
        CancellationToken ct)
    {
        topicRegistry.TaskTopics ??= [];
        if (topicRegistry.TaskTopics.TryGetValue(monitorId, out var existing) && existing > 0)
            return existing;

        var topicName = BuildMonitorTopicName(source);
        var threadId = await CreateTopic(chatId, topicName, ct);
        topicRegistry.TaskTopics[monitorId] = threadId;
        await SaveTopicRegistryAsync(topicRegistry, ct);
        return threadId;
    }

    private static Dictionary<string, string> ExtractPreferenceUpdates(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (!PreferenceIntentRegex.IsMatch(text))
            return [];

        var preferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Regex.IsMatch(text, @"\b(celsius|centigrade|metric)\b", RegexOptions.IgnoreCase))
            preferences["weather.temperature_unit"] = "celsius";
        else if (Regex.IsMatch(text, @"\b(fahrenheit|imperial)\b", RegexOptions.IgnoreCase))
            preferences["weather.temperature_unit"] = "fahrenheit";

        if (Regex.IsMatch(text, @"\b(24h|24-hour|24 hour)\b", RegexOptions.IgnoreCase))
            preferences["time.format"] = "24h";
        else if (Regex.IsMatch(text, @"\b(12h|12-hour|12 hour|am\/pm)\b", RegexOptions.IgnoreCase))
            preferences["time.format"] = "12h";

        if (Regex.IsMatch(text, @"\b(short|brief|concise)\b", RegexOptions.IgnoreCase))
            preferences["assistant.response_style"] = "concise";
        else if (Regex.IsMatch(text, @"\b(detailed|long|verbose)\b", RegexOptions.IgnoreCase))
            preferences["assistant.response_style"] = "detailed";

        return preferences;
    }

    private static string BuildAssistantInput(string userMessage, IReadOnlyDictionary<string, string> preferences)
    {
        if (preferences.Count == 0)
            return userMessage;

        var builder = new StringBuilder();
        builder.AppendLine("Use these user preferences when relevant:");
        foreach (var (key, value) in preferences.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"- {key}: {value}");

        builder.AppendLine();
        builder.Append("User message: ").Append(userMessage);
        return builder.ToString();
    }

    private static string GetPreferenceStateKey(long chatId, long? fromUserId)
    {
        var principal = fromUserId?.ToString(CultureInfo.InvariantCulture) ?? $"chat-{chatId}";
        return $"telegram:preferences:{chatId}:{principal}";
    }

    private static bool TryParseMonitoringIntent(string text, out MonitorIntent intent)
    {
        intent = default;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!TrackingKeywordRegex.IsMatch(text))
            return false;

        var intervalMatch = TrackingIntervalRegex.Match(text);
        if (!intervalMatch.Success)
            return false;

        if (!int.TryParse(intervalMatch.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ||
            value <= 0)
        {
            return false;
        }

        var unit = intervalMatch.Groups["unit"].Value;
        var requestedSeconds = unit.StartsWith('m', StringComparison.OrdinalIgnoreCase)
            ? value * 60
            : value;

        var intervalSeconds = Math.Max(requestedSeconds, MinimumMonitorIntervalSeconds);
        var source = ExtractMonitorSource(text, intervalMatch.Index);

        intent = new MonitorIntent(source, text.Trim(), requestedSeconds, intervalSeconds);
        return true;
    }

    private static string ExtractMonitorSource(string text, int intervalStartIndex)
    {
        if (intervalStartIndex <= 0 || intervalStartIndex > text.Length)
            return "custom feed";

        var beforeEvery = text[..intervalStartIndex].Trim();

        var fromMatch = Regex.Match(beforeEvery, @"\bfrom\s+(?<source>.+)$", RegexOptions.IgnoreCase);
        if (fromMatch.Success)
            return NormalizeSourceLabel(fromMatch.Groups["source"].Value);

        var commandMatch = Regex.Match(
            beforeEvery,
            @"\b(?:start\s+)?(?:track|tracking|traching|monitor|watch)(?:\s+new\s+posts?)?\s+(?<source>.+)$",
            RegexOptions.IgnoreCase);

        if (commandMatch.Success)
            return NormalizeSourceLabel(commandMatch.Groups["source"].Value);

        return "custom feed";
    }

    private static string NormalizeSourceLabel(string source)
    {
        var normalized = source.Trim().Trim('.', '!', '?', ',', ';', ':');
        return string.IsNullOrWhiteSpace(normalized) ? "custom feed" : normalized;
    }

    private static string BuildSourceKey(string providerId, string source)
    {
        var normalizedProvider = string.IsNullOrWhiteSpace(providerId)
            ? MonitorProviderGrainId
            : providerId.Trim().ToLowerInvariant();

        var normalizedSource = Regex.Replace(source ?? string.Empty, @"\s+", " ")
            .Trim()
            .ToLowerInvariant();

        return $"{normalizedProvider}:{normalizedSource}";
    }

    private static bool TryParseMonitorCallback(string callbackData, out string action, out string monitorId)
    {
        action = string.Empty;
        monitorId = string.Empty;

        if (string.IsNullOrWhiteSpace(callbackData))
            return false;

        var parts = callbackData.Split(':', 3, StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !string.Equals(parts[0], "monitor", StringComparison.OrdinalIgnoreCase))
            return false;

        action = parts[1].ToLowerInvariant();
        monitorId = parts[2];
        return monitorId.Length > 0;
    }

    private static bool IsNotificationsSummaryRequest(string text)
        => Regex.IsMatch(text, @"\b(status|summary|overview|show|list)\b", RegexOptions.IgnoreCase);

    private static string BuildMonitorCardText(TelegramMonitorState monitor, AgentTrackingStatus status)
    {
        var liveTickCount = monitor.TickOffset + status.TickCount;
        var totalTicks = Math.Max(monitor.TotalTickCount, liveTickCount);
        var remaining = Math.Max(monitor.MaxTicks - totalTicks, 0);

        var lifecycle = monitor.Lifecycle switch
        {
            MonitorLifecycle.Running => "running",
            MonitorLifecycle.Paused => "paused",
            _ => "stopped"
        };

        var builder = new StringBuilder();
        builder.AppendLine($"Monitor #{monitor.MonitorId}");
        builder.AppendLine($"Source: {monitor.Source}");
        builder.AppendLine($"Status: {lifecycle}");
        builder.AppendLine($"Interval: every {monitor.IntervalSeconds}s");
        builder.AppendLine($"Checks: {totalTicks}/{monitor.MaxTicks} (remaining {remaining})");
        builder.AppendLine($"Last check: {FormatRelativeTime(monitor.LastCheckedAtUtc)}");
        builder.AppendLine($"Last signal time: {FormatRelativeTime(monitor.LastSignalAtUtc)}");
        builder.AppendLine($"Signal: {monitor.LastStatusText}");

        return builder.ToString().TrimEnd();
    }

    private static TelegramInlineButton[][] BuildMonitorButtons(TelegramMonitorState monitor)
    {
        var primaryAction = monitor.Lifecycle switch
        {
            MonitorLifecycle.Running => new TelegramInlineButton { Text = "Pause", CallbackData = $"monitor:pause:{monitor.MonitorId}" },
            MonitorLifecycle.Paused => new TelegramInlineButton { Text = "Resume", CallbackData = $"monitor:resume:{monitor.MonitorId}" },
            _ => new TelegramInlineButton { Text = "Resume", CallbackData = $"monitor:resume:{monitor.MonitorId}" }
        };

        return
        [
            [primaryAction, new() { Text = "Refresh", CallbackData = $"monitor:refresh:{monitor.MonitorId}" }],
            [new() { Text = "🛑 Unsubscribe", CallbackData = $"monitor:stop:{monitor.MonitorId}" }]
        ];
    }

    private static TelegramInlineButton[][] BuildMonitorSignalButtons(string monitorId)
    {
        return
        [
            [new() { Text = "Refresh", CallbackData = $"monitor:refresh:{monitorId}" }],
            [new() { Text = "🛑 Unsubscribe", CallbackData = $"monitor:stop:{monitorId}" }]
        ];
    }

    private static string BuildMonitorTopicName(string source)
    {
        var compact = source.Replace('\n', ' ').Trim();
        if (compact.Length > 40)
            compact = compact[..40].TrimEnd() + "...";

        return $"Track: {compact}";
    }

    private static string FormatRelativeTime(DateTimeOffset? timestampUtc)
    {
        if (timestampUtc is null)
            return "not checked yet";

        var delta = DateTimeOffset.UtcNow - timestampUtc.Value;
        if (delta < TimeSpan.FromMinutes(1))
            return "just now";

        if (delta < TimeSpan.FromHours(1))
            return $"{Math.Max(1, (int)delta.TotalMinutes)}m ago";

        if (delta < TimeSpan.FromDays(1))
            return $"{Math.Max(1, (int)delta.TotalHours)}h ago";

        return $"{Math.Max(1, (int)delta.TotalDays)}d ago";
    }

    private sealed class TelegramMonitorRegistry
    {
        public Dictionary<string, TelegramMonitorState> Monitors { get; set; } = [];
    }

    private sealed class TelegramSourceSubscriptionRegistry
    {
        public Dictionary<string, TelegramSourceSubscriptionState> Sources { get; set; } = [];
    }

    private sealed class TelegramSourceSubscriptionState
    {
        public string SourceKey { get; set; } = string.Empty;
        public string ProviderId { get; set; } = MonitorProviderGrainId;
        public string Source { get; set; } = string.Empty;
        public string RawQuery { get; set; } = string.Empty;
        public string? Cursor { get; set; }
        public DateTimeOffset? LastCheckedAtUtc { get; set; }
        public string LastStatusText { get; set; } = string.Empty;
        public List<string> MonitorIds { get; set; } = [];
    }

    private sealed class TelegramMonitorState
    {
        public string MonitorId { get; set; } = string.Empty;
        public string AgentId { get; set; } = string.Empty;
        public string ProviderId { get; set; } = MonitorProviderGrainId;
        public string SourceKey { get; set; } = string.Empty;
        public long ChatId { get; set; }
        public int ThreadId { get; set; }
        public int CardMessageId { get; set; }
        public string Source { get; set; } = string.Empty;
        public string RawQuery { get; set; } = string.Empty;
        public string? SourceCursor { get; set; }
        public int IntervalSeconds { get; set; }
        public int RequestedIntervalSeconds { get; set; }
        public int MaxTicks { get; set; }
        public int TickOffset { get; set; }
        public int TotalTickCount { get; set; }
        public MonitorLifecycle Lifecycle { get; set; } = MonitorLifecycle.Running;
        public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastCheckedAtUtc { get; set; }
        public DateTimeOffset? LastSignalAtUtc { get; set; }
        public string LastStatusText { get; set; } = string.Empty;
        public bool CompletionNotified { get; set; }
    }

    private sealed class PreferenceChangeEvent
    {
        public long ChatId { get; set; }
        public long? UserId { get; set; }
        public Dictionary<string, string> Changes { get; set; } = [];
        public DateTimeOffset UpdatedAtUtc { get; set; }
    }

    private readonly record struct MonitorIntent(
        string Source,
        string RawQuery,
        int RequestedIntervalSeconds,
        int IntervalSeconds);

    private enum MonitorLifecycle
    {
        Running = 0,
        Paused = 1,
        Stopped = 2
    }

    private static ActivityContext? TryCreateParentActivityContext(TelegramBotUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.TraceId) || string.IsNullOrWhiteSpace(update.ParentSpanId))
            return null;

        var traceParent = $"00-{update.TraceId}-{update.ParentSpanId}-01";
        if (!ActivityContext.TryParse(traceParent, null, out var parentContext))
            return null;

        var traceFlags = update.TraceSampled ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
        return new ActivityContext(
            parentContext.TraceId,
            parentContext.SpanId,
            traceFlags,
            parentContext.TraceState);
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
