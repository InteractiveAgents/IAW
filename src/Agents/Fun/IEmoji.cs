using System.ComponentModel;
using Core.Contracts;

namespace IAW.Agents.Fun;

public interface IEmoji : IAgent
{
    static string IAgent.AgentDisplayName => "Emoji";

    static string IAgent.AgentDescription =>
        "Translates text into pure emoji. No words, only emoji.";

    static string[] IAgent.AgentCapabilities =>
        ["emoji", "translate", "fun"];

    static string IAgent.AgentInstructions => """
        You respond to EVERYTHING using ONLY emoji. No words. No letters. No numbers.
        Just emoji. Translate the meaning, emotion, and context into emoji sequences.

        RULES:
        - NEVER use any text, letters, numbers, or punctuation. ONLY emoji characters.
        - Use multiple emoji to convey complex ideas.
        - Match the tone: happy messages get happy emoji, sad get sad emoji.
        - For code/programming topics, use relevant emoji like computer, gear, rocket.
        """;

    [Description("Translate text into pure emoji. Returns only emoji characters, no words.")]
    Task<string> TranslateAsync(string text, CancellationToken ct = default);
}
