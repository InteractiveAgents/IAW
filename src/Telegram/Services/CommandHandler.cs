using Core;
using Core.Contracts;
using IAW.Agents.Orchestration;
using System.Text;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableTypes;

namespace TelegramClient.Services;

public sealed class CommandHandler(
    IClusterClient clusterClient,
    TelegramMessageSender messageSender,
    ILogger<CommandHandler> logger)
{
    static readonly int ColorPurple = 0xCB86DB;
    static readonly int ColorBlue = 0x6FB9F0;

    static readonly (string Slug, string Name, int Color)[] PredefinedTopics =
    [
        ("personal", "Personal", ColorPurple),
        ("iaw", "IAW", ColorBlue),
    ];

    public async Task HandleAsync(long chatId, long telegramId, int? topicId, string text, CancellationToken ct)
    {
        var command = text.Split(' ', 2)[0].ToLowerInvariant();
        switch (command)
        {
            case "/start":
                await HandleStartAsync(chatId, telegramId, ct);
                break;
            case "/clear":
                await HandleClearAsync(chatId, telegramId, topicId, ct);
                break;
            case "/status":
                await HandleStatusAsync(chatId, telegramId, topicId, ct);
                break;
            case "/newchat":
                await HandleNewChatAsync(chatId, telegramId, ct);
                break;
            case "/cleanup":
                await HandleCleanupAsync(chatId, telegramId, topicId, ct);
                break;
        }
    }

    public async Task HandleCommandCallbackAsync(long chatId, long telegramId, string action, string subCommand, int? topicId, CancellationToken ct)
    {
        switch (subCommand)
        {
            case "status" when action == "show":
                await HandleStatusAsync(chatId, telegramId, null, ct);
                break;
            case "cleanup":
                await HandleCleanupDeleteAsync(chatId, telegramId, action, topicId, ct);
                break;
        }
    }

    async Task HandleStartAsync(long chatId, long telegramId, CancellationToken ct)
    {
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());

        var prefs = await userProfile.GetPreferences(ct);
        if (prefs.ContainsKey(IAWConstants.StateKeys.SetupComplete))
        {
            await messageSender.SendTextAsync(chatId, "Already set up! Topics should be ready.");
            return;
        }

        foreach (var (slug, name, color) in PredefinedTopics)
        {
            try
            {
                var existingTopicId = await userProfile.GetTopicId(slug, ct);
                if (existingTopicId is not null) continue;

                var topic = await messageSender.CreateTopicAsync(chatId, name, iconColor: color);
                await userProfile.SetTopicId(slug, topic.MessageThreadId, ct);
                logger.LogInformation("Created topic {Name} (id: {TopicId}) for user {TelegramId}",
                    name, topic.MessageThreadId, telegramId);
            }
            catch (BotRequestException ex) when (ex.Message.Contains("TOPIC_NAME_ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Topic {Name} already exists for user {TelegramId}. Send a message there to register.", name, telegramId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create topic {Name}", name);
            }
        }

        await userProfile.RegisterProject("general", "general", ct);
        await userProfile.SetPreference(IAWConstants.StateKeys.GroupChatId, chatId.ToString(), ct);

        var welcomeText = "Welcome to IAW!\n\nYour Topics:\n- General \u2014 quick questions, overview\n- Personal \u2014 personal assistant, memories\n- IAW \u2014 project monitoring & troubleshooting\n\nUse /clear to reset conversation in any topic.\nUse /status for an overview.";
        var welcomeButtons = new InlineKeyboardMarkup([
            [new InlineKeyboardButton("Status") { CallbackData = "cmd:status:show" }]
        ]);
        var welcomeMsg = await messageSender.SendTextAsync(chatId, welcomeText, replyMarkup: welcomeButtons);

        try { await messageSender.PinMessageAsync(chatId, welcomeMsg.MessageId); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not pin welcome message"); }

        await userProfile.SetPreference(IAWConstants.StateKeys.SetupComplete, "true", ct);
        logger.LogInformation("Setup complete for user {TelegramId}", telegramId);
    }

    async Task HandleClearAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
    {
        var (thread, _) = await ThreadResolver.ResolveAsync(clusterClient, telegramId, topicId, ct);
        await thread.ClearHistory(ct);
        await messageSender.SendTextAsync(chatId, "Conversation cleared.", topicId);
    }

    async Task HandleStatusAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
    {
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
        var projects = await userProfile.GetProjects(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Status across all topics:\n");

        foreach (var proj in projects)
        {
            var grainId = $"{telegramId}/{proj.Slug}";
            var thread = clusterClient.GetGrain<IThread>(grainId);
            try
            {
                var history = await thread.GetHistory(ct);
                if (history.Count > 0)
                    sb.AppendLine($"[{proj.Slug}] {history.Count} messages");
            }
            catch { }
        }

        if (sb.Length < 40) sb.AppendLine("All quiet \u2014 no active threads.");

        await messageSender.SendTextAsync(chatId, sb.ToString(), topicId);
    }

    async Task HandleNewChatAsync(long chatId, long telegramId, CancellationToken ct)
    {
        var slug = $"chat-{Guid.NewGuid().ToString("N")[..6]}";

        try
        {
            var topic = await messageSender.CreateTopicAsync(chatId, "New Chat");
            var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
            await userProfile.SetTopicId(slug, topic.MessageThreadId, ct);
            await messageSender.SendTextAsync(chatId, "What would you like to work on?", topic.MessageThreadId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to create new chat topic");
            await messageSender.SendTextAsync(chatId, "Could not create topic. Make sure the group has Topics enabled.");
        }
    }

    async Task HandleCleanupAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
    {
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
        var projects = await userProfile.GetProjects(ct);

        var sb = new StringBuilder("Your topics:\n\n");
        var buttons = new List<InlineKeyboardButton[]>();

        foreach (var proj in projects)
        {
            if (proj.Slug is "general" or "personal" or "iaw") continue;

            var grainId = $"{telegramId}/{proj.Slug}";
            var thread = clusterClient.GetGrain<IThread>(grainId);
            try
            {
                var history = await thread.GetHistory(ct);
                var title = await thread.GetTitle(ct) ?? proj.Slug;
                sb.AppendLine($"- {title} ({history.Count} messages)");
                buttons.Add([new InlineKeyboardButton($"Delete: {title}")
                    { CallbackData = $"cmd:cleanup:{proj.Slug}" }]);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get info for topic {Slug}", proj.Slug);
            }
        }

        if (buttons.Count == 0)
        {
            sb.AppendLine("No custom topics to clean up.");
            await messageSender.SendTextAsync(chatId, sb.ToString(), topicId);
            return;
        }

        var keyboard = new InlineKeyboardMarkup([.. buttons]);
        await messageSender.SendTextAsync(chatId, sb.ToString(), topicId, keyboard);
    }

    async Task HandleCleanupDeleteAsync(long chatId, long telegramId, string slug, int? replyTopicId, CancellationToken ct)
    {
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());

        var topicId = await userProfile.GetTopicId(slug, ct);
        if (topicId.HasValue)
        {
            try { await messageSender.DeleteTopicAsync(chatId, topicId.Value); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete topic {Slug}", slug);
                await messageSender.SendTextAsync(chatId, $"Could not delete topic: {slug}", replyTopicId);
                return;
            }
        }

        var thread = clusterClient.GetGrain<IThread>($"{telegramId}/{slug}");
        await thread.ClearHistory(ct);
        await userProfile.RemoveProject(slug, ct);

        await messageSender.SendTextAsync(chatId, $"Deleted topic: {slug}", replyTopicId);
    }
}
