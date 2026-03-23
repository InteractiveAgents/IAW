using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using IAW.Core;
using Microsoft.Extensions.AI;

namespace IAW.Agents.Fun;

public class EmojiAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Claude45Haiku>] IChatClient chatClient)
    : Agent<IEmoji>(durableState, chatClient), IEmoji
{
    public async Task<string> TranslateAsync(string text, CancellationToken ct = default)
    {
        return await GetResponse($"Translate to emoji only: {text}", ct);
    }
}
