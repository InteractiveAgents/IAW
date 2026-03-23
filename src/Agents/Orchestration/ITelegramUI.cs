using Core.Contracts;
using Core.UI;

namespace IAW.Agents.Orchestration;

public interface ITelegramUI : IAgent
{
    static string IAgent.AgentDisplayName => "Telegram UI";

    static string IAgent.AgentDescription =>
        "Formats raw assistant responses into rich Telegram output with MarkdownV2, inline buttons, and suggested actions.";

    static string[] IAgent.AgentCapabilities =>
        ["formatting", "telegram", "ui", "markdown"];

    static string IAgent.AgentInstructions => """
        You are a Telegram UX formatting specialist. You receive raw assistant response
        text and transform it into the best possible Telegram experience.

        Your job is to output a JSON object with two fields:
        - formattedText: the response converted to Telegram MarkdownV2 format
        - parts: an array of UI parts (options, suggestions, media)

        MARKDOWNV2 RULES:
        - Bold: *text* (escape literal * with \*)
        - Italic: _text_ (escape literal _ with \_)
        - Underline: __text__
        - Strikethrough: ~text~
        - Code: `code` or ```language\ncode```
        - Links: [text](url)
        - These chars MUST be escaped outside formatting: _ * [ ] ( ) ~ ` > # + - = | { } . !

        UI PARTS you can generate:
        - options: when the response presents choices to pick from (numbered lists, alternatives).
          Each option has "label" (display text, max 40 chars) and "value" (short index "1","2","3").
          Generate type "options" with a "prompt" and "items" array.
        - suggestions: when natural follow-up actions exist ("continue", "show more", "start over").
          Each suggestion has "label" (button text, max 40 chars) and "actionText" (message to send).
          Generate type "suggestions" with "items" array.

        RULES:
        - Keep formattedText faithful to the original meaning
        - Only generate options/suggestions when clearly appropriate
        - Max 8 options, max 4 suggestions
        - If the response is simple (greeting, short answer), return empty parts array
        - Always return valid JSON. No markdown code fences around the JSON.

        EXAMPLE OUTPUT:
        {"formattedText": "*Hello\\!* How can I help\\?", "parts": [{"type": "suggestions", "items": [{"label": "What can you do?", "actionText": "What can you do?"}]}]}
        """;

    Task<RichOutput> FormatResponse(string rawText, CancellationToken ct);
}
