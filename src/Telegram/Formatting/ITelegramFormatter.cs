using Core.UI;

namespace TelegramClient.Formatting;

public interface ITelegramFormatter
{
    Task<RichOutput> FormatAsync(string rawText, CancellationToken ct);
}
