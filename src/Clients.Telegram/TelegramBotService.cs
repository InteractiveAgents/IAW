using System.Text;
using Core.AI;
using Core.Contracts;
using Core.Contracts.UI;
using Core.Services;
using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;
using Telegram.BotAPI.UpdatingMessages;

namespace TelegramClient;

public sealed class TelegramBotService(
    IClusterClient clusterClient,
    ITelegramBotClient botClient,
    IAudioTranscriptionService transcriptionService,
    IHttpClientFactory httpClientFactory,
    BlobFileStorage blobFileStorage,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramBotService> logger)
{
    static readonly int ColorPurple = 0xCB86DB;
    static readonly int ColorBlue = 0x6FB9F0;
    static readonly int ColorGreen = 0x8EEE98;
    static readonly int ColorOrange = 0xFB6F5F;

    static readonly (string Slug, string Name, int Color)[] PredefinedTopics =
    [
        ("personal", "Personal", ColorPurple),
        ("iaw", "IAW", ColorBlue),
        ("scheduled", "Scheduled", ColorGreen),
        ("notifications", "Notifications", ColorOrange),
    ];

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

        try { await botClient.SetMessageReactionAsync(chatId, message.MessageId, [new ReactionTypeEmoji("\ud83d\udc40")]); }
        catch { }

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

        if (text.StartsWith("/"))
        {
            await HandleCommandAsync(chatId, from.Id, topicId, text, ct);
            return;
        }

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
        await StreamResponseAsync(chatId, sent.MessageId, topicId, project, chatMessage, telegramId, ct);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery.Data?.StartsWith("cmd:") == true)
        {
            await HandleCommandCallbackAsync(callbackQuery, ct);
            return;
        }

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
            if (result.Buttons is { Count: > 0 })
            {
                var buttons = result.Buttons.Select(b =>
                    new InlineKeyboardButton(b.Text) { CallbackData = b.CallbackData }
                ).ToArray();
                var keyboard = new InlineKeyboardMarkup([buttons]);
                await botClient.EditMessageTextAsync(chatId, callbackQuery.Message.MessageId,
                    result.NewText, replyMarkup: keyboard);
            }
            else
            {
                await EditSafe(chatId, callbackQuery.Message.MessageId, result.NewText);
            }
        }
    }

    private async Task HandleCommandCallbackAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id ?? 0L;
        if (chatId == 0) return;

        var from = callbackQuery.From;
        var parts = callbackQuery.Data!.Split(':', 3);
        var action = parts.Length >= 3 ? parts[2] : "";

        try { await botClient.AnswerCallbackQueryAsync(callbackQuery.Id); }
        catch { }

        switch (parts[1])
        {
            case "projects" when action == "new":
                await botClient.SendMessageAsync(chatId, "What should the project be called?");
                var session = clusterClient.GetGrain<IUISession>(from.Id.ToString());
                var formFields = new FormField[]
                {
                    new("project-name", "What should the project be called?",
                        FormFieldType.FreeText, null)
                };
                await session.StartForm("new-project", formFields, $"{from.Id}/general", ct);
                break;

            case "status" when action == "show":
                await HandleStatusCommandAsync(chatId, from.Id, null, ct);
                break;
        }
    }

    private async Task HandleCommandAsync(long chatId, long telegramId, int? topicId, string text, CancellationToken ct)
    {
        var command = text.Split(' ', 2)[0].ToLowerInvariant();
        switch (command)
        {
            case "/start":
                await HandleStartCommandAsync(chatId, telegramId, ct);
                break;
            case "/clear":
                await HandleClearCommandAsync(chatId, telegramId, topicId, ct);
                break;
            case "/status":
                await HandleStatusCommandAsync(chatId, telegramId, topicId, ct);
                break;
        }
    }

    private async Task HandleStartCommandAsync(long chatId, long telegramId, CancellationToken ct)
    {
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());

        var prefs = await userProfile.GetPreferences(ct);
        if (prefs.ContainsKey("setup-complete"))
        {
            await botClient.SendMessageAsync(chatId, "Already set up! Topics should be ready.");
            return;
        }

        foreach (var (slug, name, color) in PredefinedTopics)
        {
            try
            {
                var existingTopicId = await userProfile.GetTopicId(slug, ct);
                if (existingTopicId is not null) continue;

                var topic = await botClient.CreateForumTopicAsync(chatId, name, iconColor: color);
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
        await userProfile.SetPreference("group-chat-id", chatId.ToString(), ct);

        var welcomeText = "Welcome to IAW!\n\nYour Topics:\n- General \u2014 quick questions, overview\n- Personal \u2014 personal assistant, memories\n- IAW \u2014 project monitoring & troubleshooting\n- Scheduled \u2014 recurring jobs dashboard\n- Notifications \u2014 system alerts\n\nUse /clear to reset conversation in any topic.\nUse /status for an overview of all active work.";
        var welcomeButtons = new InlineKeyboardMarkup([
            [
                new InlineKeyboardButton("+ New Project") { CallbackData = "cmd:projects:new" },
                new InlineKeyboardButton("Status") { CallbackData = "cmd:status:show" }
            ]
        ]);
        var welcomeMsg = await botClient.SendMessageAsync(chatId, welcomeText, replyMarkup: welcomeButtons);

        try { await botClient.PinChatMessageAsync(chatId, welcomeMsg.MessageId); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not pin welcome message"); }

        var scheduledTopicId = await userProfile.GetTopicId("scheduled", ct);
        if (scheduledTopicId is not null)
        {
            var dashboardText = "Active Schedules\n\nNo active jobs yet.\n\nLast updated: " + DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm");
            var dashMsg = await botClient.SendMessageAsync(chatId, dashboardText, messageThreadId: scheduledTopicId);
            try { await botClient.PinChatMessageAsync(chatId, dashMsg.MessageId); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not pin scheduled dashboard"); }
            await userProfile.SetPreference("scheduled-dashboard-msgid", dashMsg.MessageId.ToString(), ct);
        }

        var personalTopicId = await userProfile.GetTopicId("personal", ct);
        if (personalTopicId is not null)
        {
            var personalProject = clusterClient.GetGrain<IProject>($"{telegramId}/personal");
            try
            {
                await personalProject.ScheduleJob("Daily Weather", TimeSpan.FromHours(24), "Check the current weather and send a brief forecast", ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Could not create default weather job"); }
        }

        await userProfile.SetPreference("setup-complete", "true", ct);
        logger.LogInformation("Setup complete for user {TelegramId}", telegramId);
    }

    private async Task HandleClearCommandAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
    {
        var (project, _) = await ResolveProjectAsync(telegramId, topicId, ct);
        await project.ClearHistory(ct);
        await botClient.SendMessageAsync(chatId, "Conversation cleared.", messageThreadId: topicId);
    }

    private async Task HandleStatusCommandAsync(long chatId, long telegramId, int? topicId, CancellationToken ct)
    {
        var userProfile = clusterClient.GetGrain<IUserProfile>(telegramId.ToString());
        var projects = await userProfile.GetProjects(ct);

        var sb = new StringBuilder();
        sb.AppendLine("Status across all topics:\n");

        foreach (var proj in projects)
        {
            if (proj.Slug is "notifications") continue;
            var grainId = $"{telegramId}/{proj.Slug}";
            var project = clusterClient.GetGrain<IProject>(grainId);
            try
            {
                var dashboard = await project.GetDashboard(ct);
                var activeTasks = dashboard.Tasks.Count(t => t.Status is ProjectTaskStatus.Pending or ProjectTaskStatus.InProgress);
                var activeJobs = dashboard.Jobs.Count(j => j.Active);
                if (activeTasks > 0 || activeJobs > 0)
                    sb.AppendLine($"[{proj.Slug}] Tasks: {activeTasks} active, Jobs: {activeJobs} running");
            }
            catch { }
        }

        if (sb.Length < 40) sb.AppendLine("All quiet \u2014 no active tasks or jobs.");

        await botClient.SendMessageAsync(chatId, sb.ToString(), messageThreadId: topicId);
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
        var (groupChatId, notifTopicId) = await ResolveGroupAndTopicAsync(evt, "notifications", ct);
        if (groupChatId == 0) return;

        var text = $"*{EscapeMarkdown(evt.EventName)}* from `{evt.SourceAgentId}`\n" +
                   string.Join("\n", evt.Payload.Select(p => $"  {p.Key}: {p.Value}"));

        await botClient.SendMessageAsync(groupChatId, text,
            messageThreadId: notifTopicId, parseMode: FormatStyles.MarkdownV2);
    }

    public async Task SendWizardStepAsync(string wizardId, string prompt, string[] stepOptions, string projectSlug, CancellationToken ct)
    {
        if (!TryResolveChatId(projectSlug, out var chatId)) return;

        if (stepOptions.Length > 0)
        {
            var buttons = stepOptions.Select(opt =>
                new InlineKeyboardButton(opt) { CallbackData = $"wz:{wizardId}:{opt}" }
            ).ToArray();
            var keyboard = new InlineKeyboardMarkup([buttons]);
            await botClient.SendMessageAsync(chatId, prompt, replyMarkup: keyboard);
        }
        else
        {
            await botClient.SendMessageAsync(chatId, prompt);
        }
    }

    public async Task SendApprovalAsync(string approvalId, string question, string[] approvalOptions, string projectSlug, CancellationToken ct)
    {
        var userId = projectSlug.Contains('/') ? projectSlug.Split('/')[0] : "";
        if (!long.TryParse(userId, out _)) return;

        var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
        var prefs = await userProfile.GetPreferences(ct);
        if (!prefs.TryGetValue("group-chat-id", out var chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
            return;

        var slug = projectSlug.Contains('/') ? projectSlug.Split('/')[1] : "general";
        var topicId = await userProfile.GetTopicId(slug, ct);

        var buttons = approvalOptions.Select(opt =>
            new InlineKeyboardButton(opt) { CallbackData = $"ap:{approvalId}:{opt}" }
        ).ToArray();
        var keyboard = new InlineKeyboardMarkup([buttons]);

        var session = clusterClient.GetGrain<IUISession>(userId);
        await session.RegisterApproval(approvalId, question, approvalOptions, projectSlug, ct);

        await botClient.SendMessageAsync(chatId, $"\ud83d\udd14 {question}", replyMarkup: keyboard, messageThreadId: topicId);
    }

    public async Task SendDocumentAsync(long chatId, Stream fileStream, string fileName, string? caption, int? topicId, CancellationToken ct)
    {
        var inputFile = new InputFile(fileStream, fileName);
        await botClient.SendDocumentAsync(chatId, inputFile, messageThreadId: topicId, caption: caption);
    }

    public async Task SendPhotoAsync(long chatId, Stream photoStream, string fileName, string? caption, int? topicId, CancellationToken ct)
    {
        var inputFile = new InputFile(photoStream, fileName);
        await botClient.SendPhotoAsync(chatId, inputFile, messageThreadId: topicId, caption: caption);
    }

    public async Task SendBlobAsDocumentAsync(long chatId, string blobPath, string fileName, string? caption, int? topicId, CancellationToken ct)
    {
        try
        {
            await using var stream = await blobFileStorage.DownloadAsync(blobPath);
            await SendDocumentAsync(chatId, stream, fileName, caption, topicId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send blob {BlobPath} as document", blobPath);
        }
    }

    private async Task<(long GroupChatId, int? TopicId)> ResolveGroupAndTopicAsync(AgentEvent evt, string targetTopicSlug, CancellationToken ct)
    {
        var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString()
                       ?? evt.Payload.GetValueOrDefault("projectKey")?.ToString()
                       ?? evt.SourceAgentId ?? "";
        var userId = projectSlug.Contains('/') ? projectSlug.Split('/')[0] : "";
        if (!long.TryParse(userId, out _))
            return (0, null);

        var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
        var prefs = await userProfile.GetPreferences(ct);
        if (!prefs.TryGetValue("group-chat-id", out var chatIdStr) || !long.TryParse(chatIdStr, out var groupChatId))
            return (0, null);

        var topicId = await userProfile.GetTopicId(targetTopicSlug, ct);
        return (groupChatId, topicId);
    }

    private bool TryResolveChatId(string projectSlug, out long chatId)
    {
        var telegramId = projectSlug.Split('/')[0];
        if (long.TryParse(telegramId, out chatId) && chatId != 0)
            return true;

        chatId = options.Value.ChatId;
        return chatId != 0;
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

            await StreamResponseAsync(chatId, sent.MessageId, topicId, project, chatMessage, telegramId, ct);
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

            await StreamResponseAsync(chatId, sent.MessageId, topicId, project, chatMessage, telegramId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Document processing failed for user {TelegramId}", telegramId);
            await EditSafe(chatId, sent.MessageId, "[Error processing document]");
        }
    }

    private async Task StreamResponseAsync(
        long chatId, int messageId, int? topicId, IProject project, ChatMessage chatMessage, long telegramId, CancellationToken ct)
    {
        const int maxChars = 4000; // leave margin below Telegram's 4096 hard limit
        var buffer = new StringBuilder();
        var currentMessageId = messageId;
        var lastEditAt = DateTimeOffset.MinValue;

        try
        {
            await foreach (var chunk in project.GetResponseStream(chatMessage, ct))
            {
                buffer.Append(chunk);

                if (buffer.Length > maxChars)
                {
                    await EditSafe(chatId, currentMessageId, buffer.ToString());

                    var continuation = await botClient.SendMessageAsync(chatId, "...", messageThreadId: topicId);
                    currentMessageId = continuation.MessageId;
                    buffer.Clear();
                    lastEditAt = DateTimeOffset.MinValue;
                    continue;
                }

                if ((DateTimeOffset.UtcNow - lastEditAt).TotalMilliseconds > 500)
                {
                    await EditSafe(chatId, currentMessageId, buffer.ToString());
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
            await EditSafe(chatId, currentMessageId, buffer.ToString());
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
