using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Fun;

public class GreetingAgent([AgentState] AgentDurableState d, [Llm<Claude45Haiku>] IChatClient c)
    : Agent<IGreeting>(d, c), IGreeting
{
}
