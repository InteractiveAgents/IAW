using Core.Contracts;

namespace Core.Agents;

internal sealed class ChatReducer
{
    const int MaxMessageChars = 8000;
    const int MaxTotalChars = 400_000;

    public IReadOnlyList<ChatMessage> Reduce(
        IReadOnlyList<ChatMessage> fullHistory,
        ChatMessage? summary,
        int recentWindow = 20)
    {
        var result = new List<ChatMessage>();

        if (summary is not null)
            result.Add(TruncateMessage(summary));

        var recentStart = Math.Max(0, fullHistory.Count - recentWindow);

        for (var i = 0; i < recentStart; i++)
        {
            if (IsNonReducible(fullHistory[i]))
                result.Add(TruncateMessage(EvictImages(fullHistory[i])));
        }

        for (var i = recentStart; i < fullHistory.Count; i++)
            result.Add(TruncateMessage(fullHistory[i]));

        // token budget enforcement — drop oldest non-summary messages
        var totalChars = result.Sum(m => m.Text.Length);
        while (totalChars > MaxTotalChars && result.Count > 2)
        {
            var removed = result[1];
            result.RemoveAt(1);
            totalChars -= removed.Text.Length;
        }

        return result;
    }

    static ChatMessage EvictImages(ChatMessage message)
    {
        if (!message.Parts.Any(p => p is ImageContent))
            return message;

        var evictedParts = message.Parts.Select<ContentPart, ContentPart>(p => p switch
        {
            ImageContent ic => new TextContent($"[Image: {ic.Caption ?? ic.MimeType}]"),
            _ => p
        }).ToList();

        return message with { Parts = evictedParts };
    }

    static ChatMessage TruncateMessage(ChatMessage message)
    {
        var text = message.Text;
        if (text.Length <= MaxMessageChars) return message;

        var keepEach = MaxMessageChars / 2 - 50;
        var truncated = string.Concat(
            text.AsSpan(0, keepEach),
            "\n\n[...truncated...]\n\n",
            text.AsSpan(text.Length - keepEach));
        return message with
        {
            Content = truncated,
            Parts = [new TextContent(truncated)]
        };
    }

    public static bool IsNonReducible(ChatMessage message)
    {
        if (message.Parts.Any(p => p is FileContent))
            return true;

        if (message.Parts.Any(p => p is ImageContent))
            return true;

        var text = message.Text;

        if (text.Contains("remember", StringComparison.OrdinalIgnoreCase))
            return true;

        if (text.Contains("approval", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}