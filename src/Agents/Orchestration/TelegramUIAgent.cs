using Core.AI;
using Core.Contracts;
using Core.Services;
using Core.UI;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IAW.Agents.Orchestration;

[GrainType("telegram-ui")]
public class TelegramUIAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Fast>] IChatClient chatClient,
    ILogger<TelegramUIAgent> logger)
    : Agent<ITelegramUI>(durableState, chatClient), ITelegramUI
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected override int MaxHistoryMessages => 0;
    protected override IReadOnlyList<AITool> DefineTools() => [];
    protected override IReadOnlyList<AITool> DefineAdditionalTools() => [];

    public async Task<RichOutput> FormatResponse(string rawText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return new RichOutput("", []);

        try
        {
            var messages = new List<Microsoft.Extensions.AI.ChatMessage>
            {
                new(ChatRole.System, Instructions),
                new(ChatRole.User, $"Format this response for Telegram. Return ONLY valid JSON.\n\nRESPONSE TEXT:\n{rawText}")
            };

            var response = await ChatClient.GetResponseAsync(messages, new ChatOptions
            {
                MaxOutputTokens = 2048
            }, ct);

            return ParseResponse(response.Text ?? "", rawText);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "TelegramUI formatting failed, returning plain text");
            return new RichOutput(rawText, []);
        }
    }

    static RichOutput ParseResponse(string llmResponse, string fallbackText)
    {
        try
        {
            // strip markdown code fences if present
            var json = llmResponse.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                if (firstNewline > 0) json = json[(firstNewline + 1)..];
                if (json.EndsWith("```")) json = json[..^3];
                json = json.Trim();
            }

            var dto = JsonSerializer.Deserialize<FormattedResponseDto>(json, JsonOpts);
            if (dto is null)
                return new RichOutput(fallbackText, []);

            var formattedText = dto.FormattedText ?? fallbackText;
            var parts = new List<UIPart>();

            if (dto.Parts is not null)
            {
                foreach (var part in dto.Parts)
                {
                    switch (part.Type)
                    {
                        case "options" when part.Items is { Count: >= 2 }:
                        {
                            var callbackId = $"opt-{Guid.NewGuid().ToString("N")[..8]}";
                            var options = new List<Option>();
                            var idx = 1;
                            foreach (var item in part.Items)
                            {
                                if (!string.IsNullOrEmpty(item.Label))
                                    options.Add(new Option(item.Label, idx.ToString()));
                                idx++;
                                if (options.Count >= 8) break;
                            }
                            if (options.Count >= 2)
                                parts.Add(new OptionsPart(part.Prompt ?? "", options, callbackId));
                            break;
                        }

                        case "suggestions" when part.Items is { Count: > 0 }:
                        {
                            var callbackId = $"sug-{Guid.NewGuid().ToString("N")[..8]}";
                            var actions = new List<SuggestedAction>();
                            foreach (var item in part.Items)
                            {
                                if (!string.IsNullOrEmpty(item.Label))
                                    actions.Add(new SuggestedAction(item.Label, item.ActionText ?? item.Label));
                                if (actions.Count >= 4) break;
                            }
                            if (actions.Count > 0)
                                parts.Add(new SuggestionPart(callbackId, actions));
                            break;
                        }

                        case "media" when !string.IsNullOrEmpty(part.Url):
                        {
                            var fileName = part.FileName ?? Path.GetFileName(new Uri(part.Url).LocalPath);
                            var mimeType = part.MimeType ?? MimeTypes.GetMimeType(fileName);
                            parts.Add(new MediaPart(part.Url, fileName, mimeType, part.Caption));
                            break;
                        }
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

    // strongly-typed DTOs for JSON deserialization
    sealed record FormattedResponseDto
    {
        public string? FormattedText { get; init; }
        public List<UiPartDto>? Parts { get; init; }
    }

    sealed record UiPartDto
    {
        public string? Type { get; init; }
        public string? Prompt { get; init; }
        public List<UiItemDto>? Items { get; init; }
        public string? Url { get; init; }
        public string? FileName { get; init; }
        public string? MimeType { get; init; }
        public string? Caption { get; init; }
    }

    sealed record UiItemDto
    {
        public string? Label { get; init; }
        public string? Value { get; init; }
        public string? ActionText { get; init; }
    }
}
