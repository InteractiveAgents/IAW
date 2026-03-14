using Core.Contracts;
using Microsoft.Extensions.AI;
using ChatMessage = Core.Contracts.ChatMessage;

namespace Core.Agents;

internal sealed class HistorySummarizer(IChatClient chatClient)
{
    private const int SummarizationThreshold = 40;
    private const int RecentWindow = 20;

    private int _lastSummarizedOldEnd;

    public async Task<ChatMessage?> SummarizeIfNeededAsync(
        IReadOnlyList<ChatMessage> history,
        ChatMessage? existingSummary,
        CancellationToken ct = default)
    {
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
            return new ChatMessage
            {
                Role = "system",
                Content = $"[Conversation summary] {summaryText}",
                Parts = [new Contracts.TextContent($"[Conversation summary] {summaryText}")]
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return existingSummary;
        }
    }
}
