using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TelegramBot;

public sealed class MonitorSourceProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<MonitorSourceProvider> logger) : Grain, IMonitorSourceProvider
{
    private const string ProviderId = "rss";
    private const int CandidateItemsLimit = 50;

    private static readonly Regex XHandleRegex = new(
        @"(?:https?:\/\/)?(?:www\.)?(?:x\.com|twitter\.com)\/(?<handle>[A-Za-z0-9_]{1,15})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlainHandleRegex = new(
        @"^@?(?<handle>[A-Za-z0-9_]{1,15})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RedditUrlRegex = new(
        @"(?:https?:\/\/)?(?:www\.)?reddit\.com\/r\/(?<sub>[A-Za-z0-9_]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RedditSubRegex = new(
        @"^r\/(?<sub>[A-Za-z0-9_]+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<MonitorPollResult> PollAsync(MonitorPollRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = new MonitorPollResult
        {
            ProviderId = ProviderId,
            CheckedAtUtc = DateTimeOffset.UtcNow,
            NextCursor = request.Cursor
        };

        if (string.IsNullOrWhiteSpace(request.Source))
        {
            result.Success = false;
            result.Status = "Source is empty.";
            return result;
        }

        if (!TryResolveFeedUrl(request.Source, out var sourceKind, out var feedUrl, out var sourceLabel, out var resolveError))
        {
            result.Success = false;
            result.Status = resolveError;
            return result;
        }

        var maxItems = Math.Clamp(request.MaxItems, 1, 20);

        if (sourceKind is MonitorSourceKind.X or MonitorSourceKind.Reddit)
        {
            var mockItems = BuildMockItems(sourceKind, sourceLabel);
            if (mockItems.Count == 0)
            {
                result.Success = true;
                result.Status = $"No mock entries for {sourceLabel}.";
                return result;
            }

            var newestCursor = mockItems[0].Id;
            var newItems = ResolveNewItems(mockItems, request.Cursor, request.EmitInitialItems, maxItems, out var cursorState);

            result.Success = true;
            result.NextCursor = newestCursor;
            result.NewItems = newItems;
            result.Status = newItems.Count > 0
                ? $"Detected {newItems.Count} new mock post(s) from {sourceLabel}."
                : cursorState switch
                {
                    CursorResolutionState.Initialized => $"Mock tracking started for {sourceLabel}.",
                    CursorResolutionState.AdvancedWithoutChanges => $"No new mock posts from {sourceLabel}.",
                    CursorResolutionState.Reset => $"Mock cursor reset for {sourceLabel}.",
                    _ => $"No mock changes from {sourceLabel}."
                };

            return result;
        }

        try
        {
            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, feedUrl);
            requestMessage.Headers.UserAgent.ParseAdd("IAWTelegramBot/1.0");

            using var httpClient = httpClientFactory.CreateClient();
            using var response = await httpClient.SendAsync(requestMessage, ct);
            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.Status = $"Source request failed ({(int)response.StatusCode}).";
                return result;
            }

            var payload = await response.Content.ReadAsStringAsync(ct);
            var items = ParseFeedItems(payload)
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Take(CandidateItemsLimit)
                .ToList();

            if (items.Count == 0)
            {
                result.Success = true;
                result.Status = $"No entries found for {sourceLabel}.";
                return result;
            }

            var newestCursor = items[0].Id;
            var newItems = ResolveNewItems(items, request.Cursor, request.EmitInitialItems, maxItems, out var cursorState);

            result.Success = true;
            result.NextCursor = newestCursor;
            result.NewItems = newItems;
            result.Status = newItems.Count > 0
                ? $"Detected {newItems.Count} new post(s) from {sourceLabel}."
                : cursorState switch
                {
                    CursorResolutionState.Initialized => $"Tracking started for {sourceLabel}.",
                    CursorResolutionState.AdvancedWithoutChanges => $"No new posts from {sourceLabel}.",
                    CursorResolutionState.Reset => $"Feed cursor reset for {sourceLabel}.",
                    _ => $"No changes from {sourceLabel}."
                };

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Monitor provider poll failed for source {Source}", request.Source);
            result.Success = false;
            result.Status = "Provider request failed. Will retry on next check.";
            return result;
        }
    }

    private static bool TryResolveFeedUrl(
        string source,
        out MonitorSourceKind sourceKind,
        out string feedUrl,
        out string sourceLabel,
        out string error)
    {
        sourceKind = MonitorSourceKind.Unknown;
        sourceLabel = source.Trim();
        feedUrl = string.Empty;
        error = string.Empty;

        if (Uri.TryCreate(sourceLabel, UriKind.Absolute, out var directUri) &&
            (directUri.Scheme == Uri.UriSchemeHttp || directUri.Scheme == Uri.UriSchemeHttps))
        {
            sourceKind = MonitorSourceKind.Url;
            feedUrl = directUri.ToString();
            return true;
        }

        var xMatch = XHandleRegex.Match(sourceLabel);
        if (xMatch.Success)
        {
            var handle = xMatch.Groups["handle"].Value.ToLowerInvariant();
            sourceLabel = $"@{handle}";
            sourceKind = MonitorSourceKind.X;
            feedUrl = $"mock:x:{handle}";
            return true;
        }

        var plainMatch = PlainHandleRegex.Match(sourceLabel);
        if (plainMatch.Success)
        {
            var handle = plainMatch.Groups["handle"].Value.ToLowerInvariant();
            sourceLabel = $"@{handle}";
            sourceKind = MonitorSourceKind.X;
            feedUrl = $"mock:x:{handle}";
            return true;
        }

        var redditUrlMatch = RedditUrlRegex.Match(sourceLabel);
        if (redditUrlMatch.Success)
        {
            var sub = redditUrlMatch.Groups["sub"].Value.ToLowerInvariant();
            sourceLabel = $"r/{sub}";
            sourceKind = MonitorSourceKind.Reddit;
            feedUrl = $"mock:reddit:{sub}";
            return true;
        }

        var redditSubMatch = RedditSubRegex.Match(sourceLabel);
        if (redditSubMatch.Success)
        {
            var sub = redditSubMatch.Groups["sub"].Value.ToLowerInvariant();
            sourceLabel = $"r/{sub}";
            sourceKind = MonitorSourceKind.Reddit;
            feedUrl = $"mock:reddit:{sub}";
            return true;
        }

        error = "Unsupported source. Use an RSS/Atom URL, X handle (e.g. @elonmusk), or Reddit sub (e.g. r/dotnet).";
        return false;
    }

    private static List<MonitorFeedItem> BuildMockItems(MonitorSourceKind sourceKind, string sourceLabel)
    {
        var nowBucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var items = new List<MonitorFeedItem>(capacity: 25);

        for (var i = 0; i < 25; i++)
        {
            var bucket = nowBucket - i;
            var publishedAt = DateTimeOffset.FromUnixTimeSeconds(bucket * 60);
            var itemId = $"{sourceKind.ToString().ToLowerInvariant()}:{sourceLabel.ToLowerInvariant()}:{bucket}";

            var title = sourceKind switch
            {
                MonitorSourceKind.X => $"{sourceLabel} mock post at {publishedAt:HH:mm}",
                MonitorSourceKind.Reddit => $"{sourceLabel} mock thread at {publishedAt:HH:mm}",
                _ => $"Mock update at {publishedAt:HH:mm}"
            };

            var url = sourceKind switch
            {
                MonitorSourceKind.X => $"https://x.com/{sourceLabel.TrimStart('@')}/status/{Math.Abs(itemId.GetHashCode(StringComparison.Ordinal))}",
                MonitorSourceKind.Reddit => BuildMockRedditUrl(sourceLabel, itemId),
                _ => string.Empty
            };

            items.Add(new MonitorFeedItem
            {
                Id = itemId,
                Title = title,
                Url = url,
                PublishedAtUtc = publishedAt,
                Summary = "Mock item generated for subscription flow testing."
            });
        }

        return items;
    }

    private static string BuildMockRedditUrl(string sourceLabel, string itemId)
    {
        var sub = sourceLabel.StartsWith("r/", StringComparison.OrdinalIgnoreCase)
            ? sourceLabel[2..]
            : sourceLabel;

        return $"https://www.reddit.com/r/{sub}/comments/{Math.Abs(itemId.GetHashCode(StringComparison.Ordinal))}/mock/";
    }

    private static List<MonitorFeedItem> ParseFeedItems(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        var document = XDocument.Parse(xml);
        var root = document.Root;
        if (root is null)
            return [];

        if (string.Equals(root.Name.LocalName, "rss", StringComparison.OrdinalIgnoreCase))
            return ParseRssItems(root);

        if (string.Equals(root.Name.LocalName, "feed", StringComparison.OrdinalIgnoreCase))
            return ParseAtomItems(root);

        return document.Descendants()
            .Where(node => string.Equals(node.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(node.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
            .Select(ParseUnknownFeedItem)
            .Where(item => item is not null)
            .Cast<MonitorFeedItem>()
            .ToList();
    }

    private static List<MonitorFeedItem> ParseRssItems(XElement root)
    {
        var items = root.Descendants()
            .Where(node => string.Equals(node.Name.LocalName, "item", StringComparison.OrdinalIgnoreCase))
            .Select(node =>
            {
                var title = FindValue(node, "title");
                var link = FindValue(node, "link");
                var guid = FindValue(node, "guid");
                var description = FindValue(node, "description");
                var pubDate = ParseDate(
                    FindValue(node, "pubDate") ??
                    FindValue(node, "date") ??
                    FindValue(node, "published"));

                return new MonitorFeedItem
                {
                    Id = !string.IsNullOrWhiteSpace(guid) ? guid : !string.IsNullOrWhiteSpace(link) ? link : title ?? Guid.NewGuid().ToString("N"),
                    Title = title ?? "New post",
                    Url = link ?? string.Empty,
                    PublishedAtUtc = pubDate,
                    Summary = NormalizeSummary(description)
                };
            })
            .ToList();

        return items;
    }

    private static List<MonitorFeedItem> ParseAtomItems(XElement root)
    {
        var entries = root.Descendants()
            .Where(node => string.Equals(node.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))
            .Select(node =>
            {
                var id = FindValue(node, "id");
                var title = FindValue(node, "title");
                var summary = FindValue(node, "summary") ?? FindValue(node, "content");
                var published = ParseDate(
                    FindValue(node, "published") ??
                    FindValue(node, "updated"));

                var link = node.Elements()
                    .FirstOrDefault(linkNode =>
                        string.Equals(linkNode.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase) &&
                        (string.IsNullOrWhiteSpace((string?)linkNode.Attribute("rel")) ||
                         string.Equals((string?)linkNode.Attribute("rel"), "alternate", StringComparison.OrdinalIgnoreCase)))
                    ?.Attribute("href")?.Value ?? string.Empty;

                return new MonitorFeedItem
                {
                    Id = !string.IsNullOrWhiteSpace(id) ? id : !string.IsNullOrWhiteSpace(link) ? link : title ?? Guid.NewGuid().ToString("N"),
                    Title = title ?? "New post",
                    Url = link,
                    PublishedAtUtc = published,
                    Summary = NormalizeSummary(summary)
                };
            })
            .ToList();

        return entries;
    }

    private static MonitorFeedItem? ParseUnknownFeedItem(XElement element)
    {
        var title = FindValue(element, "title");
        var link = FindValue(element, "link");
        var id = FindValue(element, "id") ?? FindValue(element, "guid");
        var summary = FindValue(element, "summary") ?? FindValue(element, "description") ?? FindValue(element, "content");
        var published = ParseDate(
            FindValue(element, "published") ??
            FindValue(element, "updated") ??
            FindValue(element, "pubDate"));

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(link) && string.IsNullOrWhiteSpace(id))
            return null;

        return new MonitorFeedItem
        {
            Id = !string.IsNullOrWhiteSpace(id) ? id : !string.IsNullOrWhiteSpace(link) ? link : title ?? Guid.NewGuid().ToString("N"),
            Title = title ?? "New post",
            Url = link ?? string.Empty,
            PublishedAtUtc = published,
            Summary = NormalizeSummary(summary)
        };
    }

    private static List<MonitorFeedItem> ResolveNewItems(
        IReadOnlyList<MonitorFeedItem> items,
        string? cursor,
        bool emitInitialItems,
        int maxItems,
        out CursorResolutionState cursorState)
    {
        cursorState = CursorResolutionState.AdvancedWithoutChanges;

        if (items.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(cursor))
        {
            cursorState = CursorResolutionState.Initialized;
            return emitInitialItems ? items.Take(maxItems).ToList() : [];
        }

        var cursorIndex = items
            .Select((item, index) => new { item.Id, index })
            .FirstOrDefault(pair => string.Equals(pair.Id, cursor, StringComparison.Ordinal))
            ?.index ?? -1;

        if (cursorIndex < 0)
        {
            cursorState = CursorResolutionState.Reset;
            return items.Take(maxItems).ToList();
        }

        if (cursorIndex == 0)
        {
            cursorState = CursorResolutionState.AdvancedWithoutChanges;
            return [];
        }

        cursorState = CursorResolutionState.AdvancedWithoutChanges;
        return items.Take(cursorIndex).Take(maxItems).ToList();
    }

    private static string? FindValue(XElement element, string localName)
        => element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();

    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed.ToUniversalTime();

        return null;
    }

    private static string NormalizeSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return string.Empty;

        var compact = Regex.Replace(summary, @"\s+", " ").Trim();
        return compact.Length <= 220 ? compact : compact[..220].TrimEnd() + "...";
    }

    private enum CursorResolutionState
    {
        Initialized = 0,
        AdvancedWithoutChanges = 1,
        Reset = 2
    }

    private enum MonitorSourceKind
    {
        Unknown = 0,
        Url = 1,
        X = 2,
        Reddit = 3
    }
}
