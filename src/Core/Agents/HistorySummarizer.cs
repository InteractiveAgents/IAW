using Core.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace Core.Agents;

internal sealed class HistorySummarizer(
    IChatClient chatClient,
    IDurableDictionary<string, StateEntry>? durableState = null,
    ILogger? logger = null)
{
    private const int SummarizationThreshold = 40;
    private const int RecentWindow = 20;
    private const string SummaryStateKey = "__history_summary";
    private const string SummaryEndKey = "__history_summary_end";

    private int _lastSummarizedOldEnd;
    private ChatMessage? _cachedSummary;

    public async Task<ChatMessage?> SummarizeIfNeededAsync(
        IReadOnlyList<ChatMessage> history,
        ChatMessage? existingSummary,
        CancellationToken ct = default)
    {
        // restore summary from durable state on first call after reactivation
        if (_cachedSummary is null && existingSummary is null && durableState is not null)
        {
            if (durableState.TryGetValue(SummaryStateKey, out var entry))
            {
                _cachedSummary = new ChatMessage
                {
                    Role = "system",
                    Content = entry.Value.ToString()!,
                    Parts = [new Contracts.TextContent(entry.Value.ToString()!)]
                };
                if (durableState.TryGetValue(SummaryEndKey, out var endEntry)
                    && int.TryParse(endEntry.Value.ToString(), out var savedEnd))
                    _lastSummarizedOldEnd = savedEnd;
                existingSummary = _cachedSummary;
            }
        }

        if (history.Count <= SummarizationThreshold)
            return existingSummary;

        var oldEnd = history.Count - RecentWindow;

        // skip re-summarization if old window hasn't grown
        if (existingSummary is not null && oldEnd <= _lastSummarizedOldEnd)
            return existingSummary;

        var messagesToSummarize = new List<ChatMessage>();
        for (var i = 0; i < oldEnd; i++)
        {
            if (!ChatReducer.IsNonReducible(history[i]))
                messagesToSummarize.Add(history[i]);
        }

        if (messagesToSummarize.Count == 0)
            return existingSummary;

        var conversationText = string.Join("\n", messagesToSummarize.Select(m => $"{m.Role}: {m.Text}"));
        var prompt = $"""
            Summarize this conversation history concisely, preserving key decisions, task assignments, and outcomes.
            Do not include greetings or small talk.

            Conversation:
            {conversationText}
            """;

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>();
        if (existingSummary is not null)
            messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.System, $"Previous summary: {existingSummary.Text}"));
        messages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, prompt));

        try
        {
            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var summaryText = response.Text ?? "";

            _lastSummarizedOldEnd = oldEnd;
            var summary = new ChatMessage
            {
                Role = "system",
                Content = $"[Conversation summary] {summaryText}",
                Parts = [new Contracts.TextContent($"[Conversation summary] {summaryText}")]
            };

            if (durableState is not null)
            {
                durableState[SummaryStateKey] = new StateEntry(SummaryStateKey, summary.Content);
                durableState[SummaryEndKey] = new StateEntry(SummaryEndKey, oldEnd);
            }
            _cachedSummary = summary;

            return summary;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "History summarization failed, returning existing summary");
            return existingSummary;
        }
    }
}