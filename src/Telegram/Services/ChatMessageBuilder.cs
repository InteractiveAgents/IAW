using Core.Contracts;

namespace TelegramClient.Services;

public static class ChatMessageBuilder
{
    public static ChatMessage FromText(string text, int? sourceTelegramMsgId = null) => new()
    {
        Role = "user",
        Parts = new List<ContentPart> { new TextContent(text) },
        SourceTelegramMsgId = sourceTelegramMsgId
    };
}
