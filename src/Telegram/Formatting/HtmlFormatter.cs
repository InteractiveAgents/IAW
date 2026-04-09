using System.Text;
using System.Text.RegularExpressions;

namespace TelegramClient.Formatting;

// converts LLM markdown output to Telegram-compatible HTML
// Telegram supports: <b> <i> <u> <s> <code> <pre> <a> <blockquote> <tg-spoiler>
public static partial class HtmlFormatter
{
    public static string EscapeHtml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string MarkdownToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";

        // if the LLM already produced Telegram HTML, preserve it
        if (TelegramHtmlTag().IsMatch(markdown))
            return SanitizeExistingHtml(markdown);

        var lines = markdown.Split('\n');
        var sb = new StringBuilder(markdown.Length + 256);
        var inCodeBlock = false;
        var codeBlockLang = "";
        var codeContent = new StringBuilder();

        foreach (var rawLine in lines)
        {
            // fenced code blocks: ```lang ... ```
            if (rawLine.TrimStart().StartsWith("```"))
            {
                if (!inCodeBlock)
                {
                    inCodeBlock = true;
                    codeBlockLang = rawLine.TrimStart()[3..].Trim();
                    codeContent.Clear();
                }
                else
                {
                    inCodeBlock = false;
                    var escapedCode = EscapeHtml(codeContent.ToString().TrimEnd());
                    if (!string.IsNullOrEmpty(codeBlockLang))
                        sb.Append($"<pre><code class=\"language-{EscapeHtml(codeBlockLang)}\">{escapedCode}</code></pre>");
                    else
                        sb.Append($"<pre>{escapedCode}</pre>");
                    sb.Append('\n');
                }
                continue;
            }

            if (inCodeBlock)
            {
                if (codeContent.Length > 0) codeContent.Append('\n');
                codeContent.Append(rawLine);
                continue;
            }

            var line = rawLine;

            // headers: ## Title → <b>Title</b>
            if (line.TrimStart().StartsWith('#'))
            {
                var trimmed = line.TrimStart();
                var headerText = trimmed.TrimStart('#').TrimStart();
                sb.Append($"<b>{FormatInline(headerText)}</b>\n");
                continue;
            }

            // blockquotes: > text → <blockquote>text</blockquote>
            if (line.TrimStart().StartsWith("> "))
            {
                var quoteText = line.TrimStart()[2..];
                sb.Append($"<blockquote>{FormatInline(quoteText)}</blockquote>\n");
                continue;
            }

            sb.Append(FormatInline(line));
            sb.Append('\n');
        }

        // unclosed code block — emit what we have
        if (inCodeBlock && codeContent.Length > 0)
        {
            var escapedCode = EscapeHtml(codeContent.ToString().TrimEnd());
            sb.Append($"<pre>{escapedCode}</pre>\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    static string FormatInline(string text)
    {
        // escape HTML entities first, before we add our own tags
        text = EscapeHtml(text);

        // inline code: `code` → <code>code</code>
        // must run before bold/italic since backticks protect content
        text = InlineCodeRegex().Replace(text, "<code>$1</code>");

        // bold+italic: ***text*** or ___text___
        text = BoldItalicStarRegex().Replace(text, "<b><i>$1</i></b>");
        text = BoldItalicUnderRegex().Replace(text, "<b><i>$1</i></b>");

        // bold: **text** or __text__
        text = BoldStarRegex().Replace(text, "<b>$1</b>");
        text = BoldUnderRegex().Replace(text, "<b>$1</b>");

        // italic: *text* or _text_ (but not within words for underscores)
        text = ItalicStarRegex().Replace(text, "<i>$1</i>");
        text = ItalicUnderRegex().Replace(text, "<i>$1</i>");

        // strikethrough: ~~text~~
        text = StrikethroughRegex().Replace(text, "<s>$1</s>");

        // links: [text](url) — already HTML-escaped, unescape the parens for href
        text = LinkRegex().Replace(text, m =>
        {
            var linkText = m.Groups[1].Value;
            var url = m.Groups[2].Value.Replace("&amp;", "&");
            return $"<a href=\"{url}\">{linkText}</a>";
        });

        return text;
    }

    // truncates HTML to maxLength while keeping tags balanced
    public static string TruncateBalanced(string html, int maxLength = 4096)
    {
        if (html.Length <= maxLength) return html;

        var visibleChars = 0;
        var tagStack = new Stack<string>();
        var sb = new StringBuilder(maxLength + 200);

        for (var i = 0; i < html.Length; i++)
        {
            if (html[i] == '<')
            {
                var tagEnd = html.IndexOf('>', i);
                if (tagEnd < 0) break;

                var tag = html[(i + 1)..tagEnd];
                if (tag.StartsWith('/'))
                {
                    if (tagStack.Count > 0) tagStack.Pop();
                }
                else
                {
                    var tagName = tag.Split(' ', '>', '/')[0];
                    if (!tag.EndsWith('/') && tagName.Length > 0)
                        tagStack.Push(tagName);
                }

                sb.Append(html[i..(tagEnd + 1)]);
                i = tagEnd;
                continue;
            }

            if (html[i] == '&')
            {
                var semicolon = html.IndexOf(';', i);
                if (semicolon > i && semicolon - i < 8)
                {
                    sb.Append(html[i..(semicolon + 1)]);
                    i = semicolon;
                    visibleChars++;
                    if (visibleChars >= maxLength - 3) break;
                    continue;
                }
            }

            sb.Append(html[i]);
            visibleChars++;
            if (visibleChars >= maxLength - 3) break;
        }

        sb.Append("...");

        // close any open tags
        while (tagStack.Count > 0)
            sb.Append($"</{tagStack.Pop()}>");

        return sb.ToString();
    }

    // strips HTML tags not supported by Telegram, keeps supported ones
    static string SanitizeExistingHtml(string html)
    {
        // remove tags Telegram doesn't support, keep: b, i, u, s, code, pre, a, blockquote, tg-spoiler
        return UnsupportedHtmlTag().Replace(html, m => EscapeHtml(m.Value));
    }

    public static bool ContainsHtmlTags(string text) => TelegramHtmlTag().IsMatch(text);

    // detects Telegram-supported HTML tags in text
    [GeneratedRegex(@"</?(?:b|i|u|s|code|pre|a|blockquote|tg-spoiler)[\s>/]", RegexOptions.IgnoreCase)]
    private static partial Regex TelegramHtmlTag();

    // matches HTML tags NOT in the Telegram-supported set
    [GeneratedRegex(@"</?(?!/?(?:b|i|u|s|code|pre|a|blockquote|tg-spoiler)[\s>/])[a-zA-Z][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex UnsupportedHtmlTag();

    // regex patterns compiled once

    [GeneratedRegex(@"`([^`]+?)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*\*(.+?)\*\*\*")]
    private static partial Regex BoldItalicStarRegex();

    [GeneratedRegex(@"___(.+?)___")]
    private static partial Regex BoldItalicUnderRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldStarRegex();

    [GeneratedRegex(@"__(.+?)__")]
    private static partial Regex BoldUnderRegex();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)")]
    private static partial Regex ItalicStarRegex();

    [GeneratedRegex(@"(?<![a-zA-Z])_(?!_)(.+?)(?<!_)_(?![a-zA-Z_])")]
    private static partial Regex ItalicUnderRegex();

    [GeneratedRegex(@"~~(.+?)~~")]
    private static partial Regex StrikethroughRegex();

    [GeneratedRegex(@"\[([^\]]+?)\]\(([^)]+?)\)")]
    private static partial Regex LinkRegex();
}
