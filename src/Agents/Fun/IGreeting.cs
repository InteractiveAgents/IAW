using Core.Contracts;

namespace IAW.Agents.Fun;

public interface IGreeting : IAgent
{
    static string IAgent.AgentDisplayName => "Greeting";
    static string IAgent.AgentDescription => "Generates personalized greetings";
    static string[] IAgent.AgentCapabilities => ["greeting", "fun"];
    static string IAgent.AgentInstructions => "ALWAYS start every response with 'Greetings!' followed by a personalized message tailored to the user. No exceptions — every single response must begin with the exact word 'Greetings!' before anything else.";
}
