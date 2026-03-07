using System.Runtime.CompilerServices;

namespace IAW.Core;

public abstract partial class Agent
{
    public virtual async IAsyncEnumerable<AgentResponse> SendMessage(
        ChatMessage message,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in GetResponseStream(message.Content, ct))
            yield return new AgentResponse(AgentResponseKind.Text, chunk);
    }
}
