using System.Text.RegularExpressions;

namespace IAW.Agents.Orchestration;

public static partial class OptionsFallbackDetector
{
    public readonly record struct DetectedOptions(IReadOnlyList<string> Labels);

    const int MinItems = 2;
    const int MaxItems = 8;
    const int MaxLabelLength = 64;

    static readonly string[] TriggerWords = ["choose", "select", "pick", "vote", "which", "?"];

    [GeneratedRegex(@"^\s*\d+\.\s+(.+)$", RegexOptions.Multiline)]
    private static partial Regex NumberedItemRegex();

    public static DetectedOptions? TryDetect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var matches = NumberedItemRegex().Matches(text);
        if (matches.Count < MinItems || matches.Count > MaxItems)
            return null;

        var lastMatch = matches[^1];
        var afterList = text[(lastMatch.Index + lastMatch.Length)..];

        if (!FindTriggerAndVerifyTail(afterList))
            return null;

        var labels = new List<string>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var label = match.Groups[1].Value.Trim();

            if (i + 1 < matches.Count)
            {
                var matchEnd = match.Index + match.Length;
                var nextMatchStart = matches[i + 1].Index;
                var between = text[matchEnd..nextMatchStart];
                var hasNonListContent = between.Split('\n')
                    .Any(l => l.Trim().Length > 0 && !NumberedItemRegex().IsMatch(l));

                if (hasNonListContent)
                    return null;
            }

            if (label.Length > MaxLabelLength)
                return null;

            labels.Add(label);
        }

        if (labels.Count < MinItems)
            return null;

        return new DetectedOptions(labels);
    }

    static bool FindTriggerAndVerifyTail(string afterList)
    {
        int lastTriggerEnd = -1;

        foreach (var trigger in TriggerWords)
        {
            var searchPos = 0;
            while (true)
            {
                var idx = afterList.IndexOf(trigger, searchPos, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                var end = idx + trigger.Length;
                if (end > lastTriggerEnd)
                    lastTriggerEnd = end;
                searchPos = idx + 1;
            }
        }

        if (lastTriggerEnd < 0)
            return false;

        // find end of the line containing the last trigger
        var remaining = afterList[lastTriggerEnd..];
        var newlinePos = remaining.IndexOf('\n');
        var contentAfterTriggerLine = newlinePos >= 0
            ? remaining[(newlinePos + 1)..].Trim()
            : "";

        return contentAfterTriggerLine.Length == 0;
    }
}
