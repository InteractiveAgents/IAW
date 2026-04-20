namespace Core.UI;

[GenerateSerializer]
public sealed record ForwardMessageHint(
    [property: Id(0)] string TelegramMsgId,
    [property: Id(1)] DateTimeOffset CreatedAt) : UIPart;
