using Core.Contracts;

namespace Core.Agents;

internal sealed class ChatReducer
{
    public IReadOnlyList<ChatMessage> Reduce(
        IReadOnlyList<ChatMessage> fullHistory,
        ChatMessage? summary,
        int recentWindow = 20)
    {
        var result = new List<ChatMessage>();

        if (summary is not null)
            result.Add(summary);

        var recentStart = Math.Max(0, fullHistory.Count - recentWindow);

        // pin non-reducible messages from the older portion, evicting images to text placeholders
        for (var i = 0; i < recentStart; i++)
        {
            if (IsNonReducible(fullHistory[i]))
                result.Add(EvictImages(fullHistory[i]));
        }

        // add recent window verbatim
        for (var i = recentStart; i < fullHistory.Count; i++)
            result.Add(fullHistory[i]);

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
