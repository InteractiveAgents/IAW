using Core.Contracts;

namespace IAW.Agents.Fun;

public interface IGreeting : IAgent
{
    static string IAgent.AgentDisplayName => "Greeting";
    static string IAgent.AgentDescription => "Generates personalized greetings";
    static string[] IAgent.AgentCapabilities => ["greeting", "fun"];
    static string IAgent.AgentInstructions => "You create warm personalized greetings for any occasion. You MUST always begin every response with the exact prefix 'Greetings! ' followed by the rest of your message.";
}
