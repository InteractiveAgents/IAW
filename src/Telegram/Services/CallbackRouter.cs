using Core.Contracts;
using Core.Contracts.Security;
using Core.Contracts.UI;
using IAW.Agents.Orchestration;
using Telegram.BotAPI.AvailableTypes;

namespace TelegramClient.Services;

public sealed class CallbackRouter(
    IClusterClient clusterClient,
    TelegramMessageSender messageSender,
    CommandHandler commandHandler,
    NotificationService notificationService)
{
    // set by TelegramBotService after construction to break circular dependency
    public Func<long, int, int?, IThread, Core.Contracts.ChatMessage, long, CancellationToken, string?, Task>? StreamResponse { get; set; }

    public async Task HandleAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        if (callbackQuery.Data?.StartsWith("cmd:") == true)
        {
            await HandleCommandCallbackAsync(callbackQuery, ct);
            return;
        }

        if (callbackQuery.Data?.StartsWith("ap:") == true)
        {
            await HandleApprovalCallbackAsync(callbackQuery, ct);
            return;
        }

        var from = callbackQuery.From;
        var chatId = callbackQuery.Message?.Chat.Id ?? 0L;
        if (chatId == 0) return;

        var session = clusterClient.GetGrain<IUISession>(from.Id.ToString());
        var result = await session.HandleCallback(callbackQuery.Id, callbackQuery.Data ?? "", ct);

        await messageSender.AnswerCallbackAsync(callbackQuery.Id, result.Toast);

        if (result.NewText is not null && callbackQuery.Message is not null)
        {
            if (result.Buttons is { Count: > 0 })
            {
                var buttons = result.Buttons.Select(b =>
                    new InlineKeyboardButton(b.Text) { CallbackData = b.CallbackData }
                ).ToArray();
                var keyboard = new InlineKeyboardMarkup([buttons]);
                await messageSender.EditTextAsync(chatId, callbackQuery.Message.MessageId,
                    result.NewText, keyboard);
            }
            else
            {
                await messageSender.EditTextAsync(chatId, callbackQuery.Message.MessageId, result.NewText);
            }
        }

        if (callbackQuery.Data?.StartsWith("opt:") == true && result.Action is not null)
        {
            await HandleOptionSelectionAsync(callbackQuery, result, from.Id, chatId, ct);
        }
    }

    async Task HandleOptionSelectionAsync(CallbackQuery callbackQuery, CallbackResult result, long telegramId, long chatId, CancellationToken ct)
    {
        if (StreamResponse is null) return;

        var topicId = (callbackQuery.Message as Message)?.MessageThreadId;
        var (thread, _) = await ThreadResolver.ResolveAsync(clusterClient, telegramId, topicId, ct);

        if (result.Action!.StartsWith("suggestion:"))
        {
            var selectedLabel = result.NewText?.Contains('\u2014') == true
                ? result.NewText.Split('\u2014', 2).Last().Trim()
                : result.Action.Replace("suggestion:", "");

            var selectionMessage = ChatMessageBuilder.FromText(selectedLabel);
            var sent = await messageSender.SendTextAsync(chatId, "...", topicId);
            await StreamResponse(chatId, sent.MessageId, topicId, thread, selectionMessage, telegramId, ct, null);
        }
        else
        {
            var optParts = callbackQuery.Data!.Split(':', 3);
            if (optParts.Length >= 3)
            {
                var selectedLabel = result.NewText?.Contains('\u2014') == true
                    ? result.NewText.Split('\u2014', 2).Last().Trim()
                    : result.Action;
                var originalPrompt = result.NewText?.Contains('\u2014') == true
                    ? result.NewText.Split('\u2014', 2).First().Replace("\u2705", "").Trim()
                    : "";
                var contextPrefix = !string.IsNullOrEmpty(originalPrompt)
                    ? $"Re: '{originalPrompt}' -- " : "";
                var selectionMessage = ChatMessageBuilder.FromText($"{contextPrefix}I choose: {selectedLabel}");

                var sent = await messageSender.SendTextAsync(chatId, "...", topicId);
                await StreamResponse(chatId, sent.MessageId, topicId, thread, selectionMessage, telegramId, ct, null);
            }
        }
    }

    async Task HandleApprovalCallbackAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var parts = callbackQuery.Data!.Split(':', 3);
        if (parts.Length < 3)
        {
            await messageSender.AnswerCallbackAsync(callbackQuery.Id, "Invalid approval callback");
            return;
        }

        var approvalId = parts[1];
        var decisionKey = parts[2];
        var clickingUserId = callbackQuery.From.Id;

        // Only the user who owns the approval can resolve it — ignore taps from anyone else.
        if (notificationService.TryGetApprovalOwner(approvalId, out var ownerUserId)
            && ownerUserId != clickingUserId)
        {
            await messageSender.AnswerCallbackAsync(callbackQuery.Id, "Not your approval");
            return;
        }

        await messageSender.AnswerCallbackAsync(callbackQuery.Id);

        var approver = clusterClient.GetGrain<IApprover>(clickingUserId.ToString());
        await approver.ResolveApproval(approvalId, decisionKey, ct);
    }

    async Task HandleCommandCallbackAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        var chatId = callbackQuery.Message?.Chat.Id ?? 0L;
        if (chatId == 0) return;

        var from = callbackQuery.From;
        var parts = callbackQuery.Data!.Split(':', 3);
        var action = parts.Length >= 3 ? parts[2] : "";

        await messageSender.AnswerCallbackAsync(callbackQuery.Id);

        var topicId = (callbackQuery.Message as Message)?.MessageThreadId;
        await commandHandler.HandleCommandCallbackAsync(chatId, from.Id, action, parts[1], topicId, ct);
    }
}
