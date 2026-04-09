using Core.Services;
using Core.UI;
using System.Text.RegularExpressions;

namespace TelegramClient.Formatting;

// deterministic extraction of UI parts from agent response text — no LLM needed
public static partial class RichContentParser
{
    public static RichOutput Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new RichOutput("", []);

        var parts = new List<UIPart>();

        // extract numbered options (e.g. "1. Do X\n2. Do Y\n3. Do Z")
        var options = ExtractNumberedOptions(text);
        if (options is not null)
            parts.Add(options);

        // extract media URLs (blob storage links)
        var mediaParts = ExtractMediaUrls(text);
        parts.AddRange(mediaParts);

        // extract suggestion phrases
        var suggestions = ExtractSuggestions(text);
        if (suggestions is not null)
            parts.Add(suggestions);

        var htmlText = HtmlFormatter.MarkdownToHtml(text);
        return new RichOutput(htmlText, parts);
    }

    public static bool NeedsRichFormatting(string text)
    {
        // numbered list: "1." or "1)" at line start
        if (NumberedListDetect().IsMatch(text)) return true;

        // lettered list: "A)" or "B." at line start — LLM fallback form
        if (LetterListDetect().IsMatch(text)) return true;

        // 3+ bullet items
        if (BulletListDetect().Matches(text).Count >= 3) return true;

        // markdown headers
        if (text.Contains("\n##")) return true;

        // explicit option/choice language
        if (text.Contains("Option 1", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("Option 2", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    static OptionsPart? ExtractNumberedOptions(string text)
    {
        var matches = NumberedOptionLine().Matches(text);
        if (matches.Count < 2)
        {
            matches = LetterOptionLine().Matches(text);
            if (matches.Count < 2) return null;
        }

        // only treat as options if items are relatively short (choices, not paragraphs)
        var items = new List<Option>();
        foreach (Match m in matches)
        {
            var label = m.Groups[2].Value.Trim();
            if (label.Length > 80) return null; // too long to be a choice label
            if (items.Count >= 8) break; // max 8 options per Telegram UX
            items.Add(new Option(
                label.Length > 40 ? label[..37] + "..." : label,
                (items.Count + 1).ToString()));
        }

        if (items.Count < 2) return null;

        // extract the prompt — text before the first numbered item
        var firstMatchStart = matches[0].Index;
        var prompt = firstMatchStart > 0
            ? text[..firstMatchStart].Trim().Split('\n')[^1]
            : "";

        var callbackId = $"opt-{Guid.NewGuid().ToString("N")[..8]}";
        return new OptionsPart(prompt, items, callbackId);
    }

    static List<MediaPart> ExtractMediaUrls(string text)
    {
        var parts = new List<MediaPart>();
        var matches = BlobUrlPattern().Matches(text);

        foreach (Match m in matches)
        {
            var url = m.Value;
            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            var mimeType = MimeTypes.GetMimeType(fileName);
            parts.Add(new MediaPart(url, fileName, mimeType, null));
        }

        return parts;
    }

    static SuggestionPart? ExtractSuggestions(string text)
    {
        // look for "Would you like to..." / "You can also..." / "Try:" patterns
        if (!SuggestionTrigger().IsMatch(text)) return null;

        // find actionable phrases after the trigger
        var actions = new List<SuggestedAction>();
        var bulletMatches = SuggestionBullet().Matches(text);

        foreach (Match m in bulletMatches)
        {
            if (actions.Count >= 4) break;
            var label = m.Groups[1].Value.Trim();
            if (label.Length > 40) label = label[..37] + "...";
            actions.Add(new SuggestedAction(label, label));
        }

        if (actions.Count == 0) return null;

        var callbackId = $"sug-{Guid.NewGuid().ToString("N")[..8]}";
        return new SuggestionPart(callbackId, actions);
    }

    // regex patterns

    [GeneratedRegex(@"(?m)^\s*\d+[\.\)]\s")]
    private static partial Regex NumberedListDetect();

    [GeneratedRegex(@"(?m)^\s*[A-Za-z][\.\)]\s")]
    private static partial Regex LetterListDetect();

    [GeneratedRegex(@"(?m)^\s*[-*\u2022]\s")]
    private static partial Regex BulletListDetect();

    [GeneratedRegex(@"(?m)^\s*(\d+)[\.\)]\s+(.+)$")]
    private static partial Regex NumberedOptionLine();

    [GeneratedRegex(@"(?m)^\s*([A-Za-z])[\.\)]\s+(.+)$")]
    private static partial Regex LetterOptionLine();

    [GeneratedRegex(@"https://[^\s""<>]+blob\.core\.windows\.net[^\s""<>]+")]
    private static partial Regex BlobUrlPattern();

    [GeneratedRegex(@"(?i)(would you like|you can also|you could|try:|here are some|suggestions?:)")]
    private static partial Regex SuggestionTrigger();

    // captures bullet items that follow suggestion triggers
    [GeneratedRegex(@"(?m)^\s*[-*\u2022]\s+(.{5,50})$")]
    private static partial Regex SuggestionBullet();
}
