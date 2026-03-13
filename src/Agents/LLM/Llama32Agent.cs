using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Microsoft.Extensions.AI;

namespace IAW.Agents.LLM;

public class Llama32Agent(
    [AgentState] AgentDurableState durableState,
    [Llm<Llama32>] IChatClient chatClient)
    : global::Core.LLM(durableState, chatClient), ILlama32
{
    protected override string DisplayName => Llama32.Instance.DisplayName;
}
