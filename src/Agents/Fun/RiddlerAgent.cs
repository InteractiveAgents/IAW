using Core.AI;
using Core.AI.Models;
using Core.Contracts;
using Core.UI;
using IAW.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
namespace IAW.Agents.Fun;
public class RiddlerAgent(
    [AgentState] AgentDurableState durableState,
    [Llm<Gpt54Mini>] IChatClient chatClient,
    ILogger<RiddlerAgent> logger)
    : Agent<IRiddler>(durableState, chatClient), IRiddler
{
    public override async Task<AgentResponse> GetRichResponse(string prompt, CancellationToken ct = default)
    {
        var riddleResponse = await GetResponse(prompt, ct);
        var lines = riddleResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var riddleText = string.Join("\n", lines.TakeWhile(l => !l.TrimStart().StartsWith("A)")));
        var optionLines = lines.Where(l => l.TrimStart().StartsWith("A)") || l.TrimStart().StartsWith("B)") || l.TrimStart().StartsWith("C)") || l.TrimStart().StartsWith("D)")).ToList();
        if (optionLines.Count < 2)
            return new AgentResponse([new TextPart(riddleResponse)]);
        var callbackId = $"riddle-{Guid.NewGuid():N}";
        var options = optionLines.Select(l => new Option(l.Trim(), l[..1])).ToList();
        return new AgentResponse([
            new TextPart(riddleText.Trim()),
            new OptionsPart("Choose your answer:", options, callbackId, false)
        ]);
    }
    public override Task<AgentResponse> HandleCallback(string callbackId, string value, CancellationToken ct = default)
    {
        logger.LogInformation("Riddler callback: {Id} = {Value}", callbackId, value);
        return Task.FromResult(new AgentResponse([new TextPart($"You chose {value}! Ask me another riddle to play again.")]));
    }
}
