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
        var messages = new List<AiChatMessage>();

        foreach (var msg in history.Skip(skip))
        {
            var role = new AiChatRole(msg.Role);

            if (msg.Parts.Count > 0)
            {
                var contents = new List<Microsoft.Extensions.AI.AIContent>();
                foreach (var part in msg.Parts)
                {
                    switch (part)
                    {
                        case Contracts.TextContent tc:
                            contents.Add(new Microsoft.Extensions.AI.TextContent(tc.Text));
                            break;
                        case ImageContent ic:
                            contents.Add(new Microsoft.Extensions.AI.TextContent(
                                $"[Image: {ic.Caption ?? ic.MimeType}]"));
                            break;
                        case FileContent fc:
                            contents.Add(new Microsoft.Extensions.AI.TextContent(
                                $"[File: {fc.FileName}{(fc.Ingested ? " (indexed)" : "")}]"));
                            break;
                    }
                }
                messages.Add(new AiChatMessage(role, contents));
            }
            else
            {
                messages.Add(new AiChatMessage(role, msg.Content ?? string.Empty));
            }
        }

        return ValueTask.FromResult<IEnumerable<AiChatMessage>>(messages);
    }

    protected override ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken cancellationToken = default)
    {
        foreach (var message in context.RequestMessages)
        {
            var text = message.Text ?? string.Empty;
            history.Add(new ChatMessage
            {
                Role = message.Role.Value,
                Content = text,
                Parts = [new Contracts.TextContent(text)]
            });
        }

        foreach (var message in context.ResponseMessages ?? [])
        {
            var text = message.Text ?? string.Empty;
            history.Add(new ChatMessage
            {
                Role = message.Role.Value,
                Content = text,
                Parts = [new Contracts.TextContent(text)]
            });
        }

        return ValueTask.CompletedTask;
    }
}
