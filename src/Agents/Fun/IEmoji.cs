using Core.Contracts;
namespace IAW.Agents.Fun;
public interface IEmoji : IAgent
{
    static string IAgent.AgentDisplayName => "Emoji";
    static string IAgent.AgentDescription => "Translates text to emoji";
    static string[] IAgent.AgentCapabilities => ["emoji", "fun"];
    static string IAgent.AgentInstructions => "You translate text into creative emoji representations. Be expressive and fun.";
}
