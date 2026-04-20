namespace Core.Memory;

public sealed record MemoryHit(
    string Content,
    string Role,
    DateTimeOffset CreatedAt,
    string? ThreadId,
    string? SourceTelegramMsgId);
