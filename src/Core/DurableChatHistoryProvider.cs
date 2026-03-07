using Microsoft.Agents.AI;
using Orleans.Journaling;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace IAW.Core;

internal sealed class DurableChatHistoryProvider(IDurableList<ChatMessage> history) : ChatHistoryProvider
{
    public override string StateKey => "orleans-durable-history";

    protected override ValueTask<IEnumerable<AiChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        IEnumerable<AiChatMessage> messages = history
            .Select(m => new AiChatMessage(new AiChatRole(m.Role), m.Content));

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
