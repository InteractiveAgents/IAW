using Core.UI;
using IAW.Agents.Orchestration;

namespace TelegramClient.Formatting;

// composes deterministic RichContentParser (fast path) with TelegramUIAgent (slow fallback)
public sealed class TelegramFormatter(
    IClusterClient clusterClient,
    ILogger<TelegramFormatter> logger) : ITelegramFormatter
{
    public async Task<RichOutput> FormatAsync(string rawText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new RichOutput("", []);

        // fast path: deterministic parsing handles 95%+ of responses
        var parsed = RichContentParser.Parse(rawText);

        if (parsed.Parts.Count > 0)
            return parsed;

        // only invoke the LLM fallback for genuinely complex content
        // that the deterministic parser couldn't extract parts from,
        // AND is long enough to warrant the cost
        if (rawText.Length < 300 || !RichContentParser.NeedsRichFormatting(rawText))
            return parsed; // already has HTML-formatted text, just no parts

        try
        {
            logger.LogInformation("TelegramFormatter falling back to LLM for {Length}-char response", rawText.Length);

            var uiAgent = clusterClient.GetGrain<ITelegramUI>($"tg-ui-{Guid.NewGuid().ToString("N")[..8]}");
            var llmResult = await uiAgent.FormatResponse(rawText, ct);

            if (llmResult.Parts.Count > 0 || !string.IsNullOrEmpty(llmResult.FormattedText))
                return llmResult;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TelegramUIAgent fallback failed, using deterministic result");
        }

        return parsed;
    }
}
