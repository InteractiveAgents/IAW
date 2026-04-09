using Core.Contracts;
using Core.Contracts.UI;
using Core.UI;
using System.Collections.Concurrent;
using System.Text;
using Telegram.BotAPI.AvailableTypes;
using TelegramClient.Formatting;

namespace TelegramClient.Services;

public sealed class ResponseStreamer(
    IClusterClient clusterClient,
    TelegramMessageSender messageSender,
    TelegramFileService fileService,
    ChatActionService chatActionService,
    ITelegramFormatter formatter,
    ILogger<ResponseStreamer> logger)
{
    const int StreamingEditIntervalMs = 1500;
    const int MaxCharsPerMessage = 4000;

    readonly ConcurrentDictionary<string, bool> _renamedTopics = new();

    // original user messageId stored for reactions
    int _userMessageId;

    public async Task StreamAsync(
        long chatId, int messageId, int? topicId, IAW.Agents.Orchestration.IThread thread,
        ChatMessage chatMessage, long telegramId, CancellationToken ct, string? slug = null,
        int userMessageId = 0)
    {
        _userMessageId = userMessageId;
        var buffer = new StringBuilder();
        var currentMessageId = messageId;
        var lastEditAt = DateTimeOffset.MinValue;
        var hasError = false;

        // show typing indicator while agent processes
        await using var typing = chatActionService.StartTyping(chatId, topicId);

        try
        {
            await foreach (var chunk in thread.GetResponseStream(chatMessage, ct))
            {
                // stop typing once we start streaming content
                if (buffer.Length == 0)
                    typing.Stop();

                buffer.Append(chunk);

                if (buffer.Length > MaxCharsPerMessage)
                {
                    await StreamingEditAsync(chatId, currentMessageId, buffer.ToString());
                    var continuation = await messageSender.SendTextAsync(chatId, "...", topicId);
                    currentMessageId = continuation.MessageId;
                    buffer.Clear();
                    lastEditAt = DateTimeOffset.MinValue;
                    continue;
                }

                if ((DateTimeOffset.UtcNow - lastEditAt).TotalMilliseconds > StreamingEditIntervalMs)
                {
                    await StreamingEditAsync(chatId, currentMessageId, buffer.ToString());
                    lastEditAt = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogWarning("Streaming cancelled for user {TelegramId}", telegramId);
            if (buffer.Length == 0) buffer.Append("[Request timed out]");
            hasError = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error streaming response from thread for user {TelegramId}", telegramId);
            buffer.Append("\n\n[Error communicating with assistant]");
            hasError = true;
        }

        var finalText = buffer.ToString();

        // explicit UI hints from ProposeOptions and similar tools take priority
        var explicitHints = await thread.GetPendingUIHints(ct);

        try
        {
            var richOutput = await formatter.FormatAsync(finalText, ct);
            var combinedParts = MergeHintsIntoParts(explicitHints, richOutput.Parts);

            if (combinedParts.Count > 0)
            {
                var merged = richOutput with { Parts = combinedParts };
                await RenderRichOutputAsync(chatId, currentMessageId, topicId, merged, telegramId, ct);
            }
            else if (!string.IsNullOrEmpty(richOutput.FormattedText))
            {
                await messageSender.EditHtmlAsync(chatId, currentMessageId, richOutput.FormattedText);
            }
            else if (finalText.Length > 0)
            {
                var html = HtmlFormatter.MarkdownToHtml(finalText);
                await messageSender.EditHtmlAsync(chatId, currentMessageId, html);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Formatting failed for user {TelegramId}, falling back to plain text", telegramId);
            if (finalText.Length > 0)
                await messageSender.EditTextAsync(chatId, currentMessageId, finalText);
        }

        await fileService.DeliverPendingAsync(chatId, topicId, () => thread.GetPendingDeliveries(ct));
        await TryAutoRenameTopicAsync(chatId, topicId, thread, slug, ct);
        await SetCompletionReactionAsync(chatId, hasError);
    }

    static List<Core.UI.UIPart> MergeHintsIntoParts(
        IReadOnlyList<Core.UI.UIPart> explicitHints,
        IReadOnlyList<Core.UI.UIPart> parsedParts)
    {
        if (explicitHints.Count == 0)
            return parsedParts.ToList();

        // explicit hints win: drop any parser-inferred OptionsPart when the agent provided one
        var hasExplicitOptions = explicitHints.Any(p => p is Core.UI.OptionsPart);
        var filteredParsed = hasExplicitOptions
            ? parsedParts.Where(p => p is not Core.UI.OptionsPart).ToList()
            : parsedParts.ToList();

        var merged = new List<Core.UI.UIPart>(filteredParsed);
        merged.AddRange(explicitHints);
        return merged;
    }

    async Task SetCompletionReactionAsync(long chatId, bool hasError)
    {
        if (_userMessageId <= 0) return;
        await messageSender.SetReactionAsync(chatId, _userMessageId, hasError ? "\u274c" : "\u2705");
    }

    async Task RenderRichOutputAsync(long chatId, int messageId, int? topicId, RichOutput richOutput, long telegramId, CancellationToken ct)
    {
        var userId = telegramId.ToString();
        var session = clusterClient.GetGrain<IUISession>(userId);

        var rows = new List<InlineKeyboardButton[]>();

        foreach (var part in richOutput.Parts)
        {
            if (part is OptionsPart optionsPart && optionsPart.Options.Count >= 2)
            {
                var pendingOpts = optionsPart.Options.Select(o => new PendingOption(o.Label, o.Value)).ToArray();
                var threadId = $"{telegramId}/{topicId?.ToString() ?? "general"}";
                await session.RegisterOptions(optionsPart.CallbackId, optionsPart.Prompt, pendingOpts, threadId, "option", ct);
                rows.Add(optionsPart.Options.Select(o =>
                    new InlineKeyboardButton(o.Label) { CallbackData = $"opt:{optionsPart.CallbackId}:{o.Value}" }
                ).ToArray());
            }

            if (part is SuggestionPart suggestionPart && suggestionPart.Actions.Count > 0)
            {
                var pendingOpts = suggestionPart.Actions.Select((a, i) => new PendingOption(a.Label, (i + 1).ToString())).ToArray();
                var threadId = $"{telegramId}/{topicId?.ToString() ?? "general"}";
                await session.RegisterOptions(suggestionPart.CallbackId, "", pendingOpts, threadId, "suggestion", ct);
                rows.Add(suggestionPart.Actions.Select((a, i) =>
                    new InlineKeyboardButton(a.Label) { CallbackData = $"opt:{suggestionPart.CallbackId}:{i + 1}" }
                ).ToArray());
            }
        }

        var keyboard = rows.Count > 0 ? new InlineKeyboardMarkup([.. rows]) : null;
        await messageSender.EditHtmlAsync(chatId, messageId, richOutput.FormattedText, keyboard);

        foreach (var part in richOutput.Parts.OfType<MediaPart>())
            await fileService.DeliverMediaAsync(chatId, topicId, [part]);

        foreach (var part in richOutput.Parts.OfType<ForwardMessageHint>())
        {
            if (int.TryParse(part.TelegramMsgId, out var sourceId))
                await messageSender.ForwardMessageAsync(chatId, sourceId, chatId, topicId);
        }
    }

    async Task StreamingEditAsync(long chatId, int messageId, string text)
    {
        // if the LLM is outputting HTML, render it properly during streaming
        if (HtmlFormatter.ContainsHtmlTags(text))
        {
            var balanced = HtmlFormatter.TruncateBalanced(text);
            await messageSender.EditHtmlAsync(chatId, messageId, balanced);
        }
        else
        {
            await messageSender.EditTextAsync(chatId, messageId, text);
        }
    }

    async Task TryAutoRenameTopicAsync(long chatId, int? topicId, IAW.Agents.Orchestration.IThread thread, string? slug, CancellationToken ct)
    {
        if (slug is null || !slug.StartsWith("chat-") || !topicId.HasValue)
            return;

        if (_renamedTopics.ContainsKey(slug))
            return;

        try
        {
            var title = await thread.GetTitle(ct);
            if (title is not null)
            {
                await messageSender.EditTopicAsync(chatId, topicId.Value, title);
                _renamedTopics.TryAdd(slug, true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Best-effort topic rename failed");
        }
    }
}
