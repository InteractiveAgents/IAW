using Core;
using Core.Contracts;
using Core.Contracts.Security;
using Core.Contracts.UI;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Telegram;
using Telegram.BotAPI.AvailableTypes;
using TelegramClient.Formatting;

namespace TelegramClient.Services;

public sealed class NotificationService(
    IClusterClient clusterClient,
    TelegramMessageSender messageSender,
    ITelegramFormatter formatter,
    IOptions<TelegramBotOptions> options,
    ILogger<NotificationService> logger)
{
    readonly ConcurrentDictionary<string, (long ChatId, int MessageId, int? TopicId)> _progressMessages = new();
    readonly ConcurrentDictionary<string, ApprovalDelivery> _approvalDeliveries = new();

    public bool TryGetApprovalOwner(string approvalId, out long ownerUserId)
    {
        if (_approvalDeliveries.TryGetValue(approvalId, out var delivery))
        {
            ownerUserId = delivery.OwnerUserId;
            return true;
        }
        ownerUserId = 0;
        return false;
    }

    sealed record ApprovalDelivery(long ChatId, int MessageId, int? TopicId, string Question, long OwnerUserId);

    public async Task SendNotificationAsync(AgentEvent evt, CancellationToken ct)
    {
        var (groupChatId, notifTopicId) = await ResolveGroupAndTopicAsync(evt, "notifications", ct);
        if (groupChatId == 0) return;

        var html = $"<b>{HtmlFormatter.EscapeHtml(evt.EventName)}</b> from <code>{HtmlFormatter.EscapeHtml(evt.SourceAgentId)}</code>\n" +
                   string.Join("\n", evt.Payload.Select(p =>
                       $"  {HtmlFormatter.EscapeHtml(p.Key)}: {HtmlFormatter.EscapeHtml(p.Value?.ToString() ?? "")}"));

        await messageSender.SendHtmlAsync(groupChatId, html, notifTopicId);
    }

    public async Task SendJobResultAsync(string projectKey, string jobName, string result, CancellationToken ct)
    {
        var parts = projectKey.Split('/');
        if (parts.Length < 2 || !long.TryParse(parts[0], out _))
        {
            logger.LogWarning("SendJobResult: invalid projectKey format '{ProjectKey}'", projectKey);
            return;
        }

        var userId = parts[0];
        var slug = parts[1];
        var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
        var prefs = await userProfile.GetPreferences(ct);
        if (!prefs.TryGetValue(IAWConstants.StateKeys.GroupChatId, out var chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            logger.LogWarning("SendJobResult: no GroupChatId for user {UserId}, slug {Slug}", userId, slug);
            return;
        }

        var topicId = await userProfile.GetTopicId(slug, ct);
        var (formattedText, orchestrationTaskId) = FormatOrchestrationResult(result);

        int messageId;
        if (orchestrationTaskId is not null && TryGetProgressMessage(orchestrationTaskId, out var progress))
        {
            try
            {
                await messageSender.EditTextAsync(progress.ChatId, progress.MessageId, formattedText);
                messageId = progress.MessageId;
                chatId = progress.ChatId;
                topicId = progress.TopicId;
            }
            catch
            {
                var sent = await messageSender.SendTextAsync(chatId, formattedText, topicId);
                messageId = sent.MessageId;
            }
        }
        else
        {
            var sent = await messageSender.SendTextAsync(chatId, formattedText, topicId);
            messageId = sent.MessageId;
        }

        if (RichContentParser.NeedsRichFormatting(formattedText))
        {
            try
            {
                var richOutput = await formatter.FormatAsync(formattedText, ct);
                if (!string.IsNullOrEmpty(richOutput.FormattedText))
                    await messageSender.EditHtmlAsync(chatId, messageId, richOutput.FormattedText);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Formatting failed for job result, keeping plain text");
            }
        }
    }

    public async Task SendProgressAsync(string projectKey, string taskId, string phase, string message, CancellationToken ct)
    {
        if (_progressMessages.TryGetValue(taskId, out var existing))
        {
            try
            {
                await messageSender.EditTextAsync(existing.ChatId, existing.MessageId, $"\u2699\ufe0f {message}");
            }
            catch { }
            return;
        }

        var parts = projectKey.Split('/');
        if (parts.Length < 2 || !long.TryParse(parts[0], out _))
        {
            logger.LogWarning("SendProgress: invalid projectKey format '{ProjectKey}'", projectKey);
            return;
        }

        var userId = parts[0];
        var slug = parts[1];
        var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
        var prefs = await userProfile.GetPreferences(ct);
        if (!prefs.TryGetValue(IAWConstants.StateKeys.GroupChatId, out var chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            logger.LogWarning("SendProgress: no GroupChatId for user {UserId}", userId);
            return;
        }

        var topicId = await userProfile.GetTopicId(slug, ct);
        var sent = await messageSender.SendTextAsync(chatId, $"\u2699\ufe0f {message}", topicId);
        _progressMessages[taskId] = (chatId, sent.MessageId, topicId);
    }

    public bool TryGetProgressMessage(string taskId, out (long ChatId, int MessageId, int? TopicId) progress)
        => _progressMessages.TryRemove(taskId, out progress);

    public async Task SendWizardStepAsync(string wizardId, string prompt, string[] stepOptions, string projectSlug, CancellationToken ct)
    {
        if (!TryResolveChatId(projectSlug, out var chatId)) return;

        if (stepOptions.Length > 0)
        {
            var buttons = stepOptions.Select(opt =>
                new InlineKeyboardButton(opt) { CallbackData = $"wz:{wizardId}:{opt}" }
            ).ToArray();
            var keyboard = new InlineKeyboardMarkup([buttons]);
            await messageSender.SendTextAsync(chatId, prompt, replyMarkup: keyboard);
        }
        else
        {
            await messageSender.SendTextAsync(chatId, prompt);
        }
    }

    public async Task SendApprovalRequestedAsync(AgentEvent evt, CancellationToken ct)
    {
        var approvalId = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.ApprovalId)?.ToString();
        var userId = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.UserId)?.ToString();
        var question = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.Question)?.ToString();
        var optionsJson = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.OptionsJson)?.ToString();

        if (string.IsNullOrEmpty(approvalId) || string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(question) || string.IsNullOrEmpty(optionsJson))
        {
            logger.LogWarning("SendApprovalRequested: payload incomplete for approval {ApprovalId}", approvalId);
            return;
        }

        if (!long.TryParse(userId, out _))
        {
            logger.LogWarning("SendApprovalRequested: userId {UserId} is not a valid chat id", userId);
            return;
        }

        var options = ParseApprovalOptions(optionsJson);
        if (options.Count == 0)
        {
            logger.LogWarning("SendApprovalRequested: no options parsed for {ApprovalId}", approvalId);
            return;
        }

        var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
        var prefs = await userProfile.GetPreferences(ct);
        if (!prefs.TryGetValue(IAWConstants.StateKeys.GroupChatId, out var chatIdStr) || !long.TryParse(chatIdStr, out var chatId))
        {
            logger.LogWarning("SendApprovalRequested: no GroupChatId preference for user {UserId}", userId);
            return;
        }

        var topicId = await userProfile.GetTopicId("general", ct);
        var keyboard = BuildApprovalKeyboard(approvalId, options);

        var sent = await messageSender.SendTextAsync(chatId, $"\ud83d\udd14 {question}", topicId, keyboard);
        _approvalDeliveries[approvalId] = new ApprovalDelivery(
            chatId, sent.MessageId, topicId, question, long.Parse(userId));
    }

    public async Task SendApprovalResolvedAsync(AgentEvent evt, CancellationToken ct)
    {
        var approvalId = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.ApprovalId)?.ToString();
        var decisionKey = evt.Payload.GetValueOrDefault(IAWConstants.PayloadKeys.DecisionKey)?.ToString();
        if (string.IsNullOrEmpty(approvalId) || string.IsNullOrEmpty(decisionKey))
            return;

        if (!_approvalDeliveries.TryRemove(approvalId, out var delivery))
            return;

        var resolvedText = $"\u2705 {delivery.Question} \u2014 {decisionKey}";
        try
        {
            await messageSender.EditTextAsync(delivery.ChatId, delivery.MessageId, resolvedText);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to edit resolved approval {ApprovalId}", approvalId);
        }
        await Task.CompletedTask;
    }

    static IReadOnlyList<ApprovalOption> ParseApprovalOptions(string optionsJson)
    {
        try
        {
            var opts = JsonSerializer.Deserialize<List<ApprovalOption>>(optionsJson);
            return opts ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    static InlineKeyboardMarkup BuildApprovalKeyboard(string approvalId, IReadOnlyList<ApprovalOption> options)
    {
        var rows = new List<InlineKeyboardButton[]>();
        foreach (var opt in options)
        {
            rows.Add([
                new InlineKeyboardButton(opt.Label) { CallbackData = $"ap:{approvalId}:{opt.Key}" }
            ]);
        }
        return new InlineKeyboardMarkup([.. rows]);
    }

    // helpers

    async Task<(long GroupChatId, int? TopicId)> ResolveGroupAndTopicAsync(AgentEvent evt, string targetTopicSlug, CancellationToken ct)
    {
        var projectSlug = evt.Payload.GetValueOrDefault("projectSlug")?.ToString()
                       ?? evt.Payload.GetValueOrDefault("projectKey")?.ToString()
                       ?? evt.SourceAgentId ?? "";
        var userId = projectSlug.Contains('/') ? projectSlug.Split('/')[0] : "";
        if (!long.TryParse(userId, out _))
            return (0, null);

        var userProfile = clusterClient.GetGrain<IUserProfile>(userId);
        var prefs = await userProfile.GetPreferences(ct);
        if (!prefs.TryGetValue(IAWConstants.StateKeys.GroupChatId, out var chatIdStr) || !long.TryParse(chatIdStr, out var groupChatId))
            return (0, null);

        var topicId = await userProfile.GetTopicId(targetTopicSlug, ct);
        return (groupChatId, topicId);
    }

    bool TryResolveChatId(string projectSlug, out long chatId)
    {
        var telegramId = projectSlug.Split('/')[0];
        if (long.TryParse(telegramId, out chatId) && chatId != 0)
            return true;

        chatId = options.Value.ChatId;
        return chatId != 0;
    }

    static (string Text, string? TaskId) FormatOrchestrationResult(string resultPayload)
    {
        try
        {
            var result = System.Text.Json.JsonSerializer.Deserialize<Core.Contracts.OrchestrationResult>(resultPayload);
            if (result is null) return (resultPayload, null);

            var sb = new StringBuilder();
            sb.AppendLine(result.Success ? $"\u2705 {result.Summary}" : $"\u274c {result.Summary}");

            foreach (var artifact in result.Artifacts)
                sb.AppendLine($"\ud83d\udcc1 {artifact}");

            if (result.Metrics is { Count: > 0 })
            {
                var metricStr = string.Join(", ", result.Metrics.Select(kv => $"{kv.Key}: {kv.Value}"));
                sb.AppendLine($"\u23f1 {metricStr}");
            }

            if (!result.Success && !string.IsNullOrEmpty(result.ErrorDetail))
            {
                var truncated = result.ErrorDetail.Length > 500 ? result.ErrorDetail[..500] + "..." : result.ErrorDetail;
                sb.AppendLine();
                sb.AppendLine(truncated);
            }

            return (sb.ToString().TrimEnd(), result.TaskId);
        }
        catch (System.Text.Json.JsonException)
        {
            return (resultPayload, null);
        }
    }
}
