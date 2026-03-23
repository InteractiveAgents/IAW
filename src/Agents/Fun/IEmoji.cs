using Core.Contracts;
using System.ComponentModel;

namespace IAW.Agents.Fun;

public interface IEmoji : IAgent
{
    static string IAgent.AgentDisplayName => "Emoji";
    static string IAgent.AgentInstructions => "Respond ONLY in emoji. No words. No letters. Just emoji.";
    [Description("Translate text to pure emoji")]
    Task<string> TranslateAsync(string text, CancellationToken ct = default);
}