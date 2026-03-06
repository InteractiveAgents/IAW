using Microsoft.Extensions.AI;
using Orleans.Journaling;

namespace Core.V3;

public interface IAgent
{
    IAsyncEnumerable<string> GetResponse(string prompt, CancellationToken cancellationToken);
}

public class Agent(IChatClient chatClient) : DurableGrain, IAgent
{
    ChatClientAgent? _agent;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _agent = chatClient.AsAIAgent("You're a zzzzzzzzzz assistant that provides information about the weather.");
        return base.OnActivateAsync(cancellationToken);
    }

    public async IAsyncEnumerable<string> GetResponse(
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var chunk in _agent!.InvokeStreamingAsync(prompt, cancellationToken: cancellationToken))
        {
            if (chunk.Text is { } text)
                yield return text;
        }
    }
}
