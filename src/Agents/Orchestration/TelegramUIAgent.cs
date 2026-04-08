using Core.AI;
using Core.Contracts;
using Core.Services;
using Core.UI;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IAW.Agents.Orchestration;

[GrainType("telegram-ui")]
public class TelegramUIAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient,
    ILogger<TelegramUIAgent> logger)
    : Agent<ITelegramUI>(durableState, chatClient), ITelegramUI
{
    // no tools, no history — pure formatting agent
    protected override int MaxHistoryMessages => 0;
    protected override IReadOnlyList<AITool> DefineTools() => [];
    protected override IReadOnlyList<AITool> DefineAdditionalTools() => [];

    public async Task<RichOutput> FormatResponse(string rawText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new RichOutput("", []);

        // deterministically extract file delivery URLs before LLM formatting
        var mediaParts = ExtractMediaParts(rawText);

        try
        {
            // bypass Agent pipeline (GetResponse) to avoid tool-calling loop:
            // DiscoverInterfaceTools registers FormatResponse itself as an LLM tool,
            // causing recursive calls and massive token waste
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(ChatRole.System, Instructions),
                new(ChatRole.User, $"Format this response for Telegram. Return ONLY valid JSON.\n\nRESPONSE TEXT:\n{rawText}")
            };

            var response = await ChatClient.GetResponseAsync(messages, new ChatOptions
            {
                MaxOutputTokens = 2048
            }, ct);

            var richOutput = ParseRichOutput(response.Text ?? "", rawText);

            if (mediaParts.Count > 0)
            {
                var allParts = new List<UIPart>(richOutput.Parts);
                allParts.AddRange(mediaParts);
                return new RichOutput(richOutput.FormattedText, allParts);
            }

            return richOutput;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TelegramUI formatting failed, returning plain text");
            return new RichOutput(rawText, mediaParts.Count > 0 ? [.. mediaParts] : []);
        }
    }

    static List<MediaPart> ExtractMediaParts(string text)
    {
        var parts = new List<MediaPart>();
        // match blob storage URLs for delivered files
        var blobPattern = @"https?://[^\s""]+\.blob\.core\.windows\.net/files/deliveries/[^\s""]+";
        foreach (Match match in Regex.Matches(text, blobPattern))
        {
            var url = match.Value.TrimEnd('.', ',', ')', ']', '>');
            try
            {
                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);
                var mimeType = MimeTypes.GetMimeType(fileName);
                parts.Add(new MediaPart(url, fileName, mimeType, fileName));
            }
            catch
            {
                // malformed URL, skip
            }
        }
        return parts;
    }

    static RichOutput ParseRichOutput(string llmResponse, string fallbackText)
    {
        try
        {
            var jsonStart = llmResponse.IndexOf('{');
            var jsonEnd = llmResponse.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < 0)
                return new RichOutput(fallbackText, []);

            var json = llmResponse[jsonStart..(jsonEnd + 1)];
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var formattedText = root.TryGetProperty("formattedText", out var ft)
                ? ft.GetString() ?? fallbackText
                : fallbackText;

            var parts = new List<UIPart>();

            if (root.TryGetProperty("parts", out var partsEl) && partsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in partsEl.EnumerateArray())
                {
                    if (!part.TryGetProperty("type", out var typeEl)) continue;
                    var partType = typeEl.GetString();

                    if (partType == "options" && part.TryGetProperty("items", out var optItems))
                    {
                        var callbackId = $"opt-{Guid.NewGuid().ToString("N")[..8]}";
                        var prompt = part.TryGetProperty("prompt", out var p) ? p.GetString() ?? "" : "";
                        var options = new List<Option>();
                        var idx = 1;
                        foreach (var item in optItems.EnumerateArray())
                        {
                            var label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                            if (label.Length > 0)
                                options.Add(new Option(label, idx.ToString()));
                            idx++;
                        }
                        if (options.Count >= 2)
                            parts.Add(new OptionsPart(prompt, options, callbackId));
                    }

                    if (partType == "media" && part.TryGetProperty("url", out var urlEl))
                    {
                        var mediaUrl = urlEl.GetString() ?? "";
                        var mediaFileName = part.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : Path.GetFileName(new Uri(mediaUrl).LocalPath);
                        var mediaMimeType = part.TryGetProperty("mimeType", out var mt) ? mt.GetString() ?? "application/octet-stream" : MimeTypes.GetMimeType(mediaFileName);
                        var mediaCaption = part.TryGetProperty("caption", out var cap) ? cap.GetString() : null;
                        if (mediaUrl.Length > 0)
                            parts.Add(new MediaPart(mediaUrl, mediaFileName, mediaMimeType, mediaCaption));
                    }

                    if (partType == "suggestions" && part.TryGetProperty("items", out var sugItems))
                    {
                        var callbackId = $"sug-{Guid.NewGuid().ToString("N")[..8]}";
                        var actions = new List<SuggestedAction>();
                        foreach (var item in sugItems.EnumerateArray())
                        {
                            var label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                            var actionText = item.TryGetProperty("actionText", out var a) ? a.GetString() ?? label : label;
                            if (label.Length > 0)
                                actions.Add(new SuggestedAction(label, actionText));
                        }
                        if (actions.Count > 0)
                            parts.Add(new SuggestionPart(callbackId, actions));
                    }
                }
            }

            return new RichOutput(formattedText, parts);
        }
        catch
        {
            return new RichOutput(fallbackText, []);
        }
    }
}