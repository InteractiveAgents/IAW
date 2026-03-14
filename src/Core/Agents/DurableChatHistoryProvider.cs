using Core.Contracts;
using Microsoft.Agents.AI;
using Orleans.Journaling;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace Core.Agents;

internal sealed class DurableChatHistoryProvider(IDurableList<ChatMessage> history, int maxMessages) : ChatHistoryProvider
{
    public override IReadOnlyList<string> StateKeys => ["orleans-durable-history"];

    protected override ValueTask<IEnumerable<AiChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        var skip = Math.Max(0, history.Count - maxMessages);
        IEnumerable<AiChatMessage> messages = history
            .Skip(skip)
            .Select(m => new AiChatMessage(new AiChatRole(m.Role), m.Text));

        return ValueTask.FromResult(messages);
    }

    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var message in context.RequestMessages)
        {
            history.Add(new ChatMessage
            {
                Role = message.Role.Value,
                Content = message.Text ?? string.Empty
            });
        }

        foreach (var message in context.ResponseMessages ?? [])
        {
            history.Add(new ChatMessage
            {
                Role = message.Role.Value,
                Content = message.Text ?? string.Empty
            });
        }

        return ValueTask.CompletedTask;
    }
}
