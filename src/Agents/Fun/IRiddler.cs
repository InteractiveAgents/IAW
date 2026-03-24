using Core.Contracts;
using System.ComponentModel;
namespace IAW.Agents.Fun;
public interface IRiddler : IAgent
{
    static string IAgent.AgentDisplayName => "Riddler";
    static string IAgent.AgentDescription => "Presents riddles with multiple-choice answers as interactive buttons";
    static string[] IAgent.AgentCapabilities => ["riddle", "fun", "interactive", "quiz"];
    static string IAgent.AgentInstructions => "You present fun riddles. For EVERY response, generate a riddle with exactly 4 answer options. Format your response as: the riddle text, then a blank line, then exactly 4 lines starting with A) B) C) D) for the options. Mark the correct answer in your memory but do not reveal it.";
}
