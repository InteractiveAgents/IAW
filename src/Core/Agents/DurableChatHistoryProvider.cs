using Core.Contracts;
using Core.Services;
using Microsoft.Agents.AI;
using Orleans.Journaling;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace Core.Agents;

internal sealed class DurableChatHistoryProvider(
    IDurableList<ChatMessage> history,
    int maxMessages,
    BlobFileStorage? blobStorage = null,
    ChatReducer? reducer = null,
    HistorySummarizer? summarizer = null) : ChatHistoryProvider
{
    private ChatMessage? _lastSummary;
    public override IReadOnlyList<string> StateKeys => ["orleans-durable-history"];

    protected override async ValueTask<IEnumerable<AiChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken cancellationToken = default)
    {
        IEnumerable<ChatMessage> sourceMessages;

        if (reducer is not null)
        {
            var allMessages = history.ToList();

            if (summarizer is not null)
                _lastSummary = await summarizer.SummarizeIfNeededAsync(allMessages, _lastSummary, cancellationToken);

            sourceMessages = reducer.Reduce(allMessages, summary: _lastSummary, recentWindow: maxMessages);
        }
        else
        {
            var skip = Math.Max(0, history.Count - maxMessages);
            sourceMessages = history.Skip(skip);
        }

        var messages = new List<AiChatMessage>();

        foreach (var msg in sourceMessages)
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
                            await AppendImageContent(contents, ic);
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

        return messages;
    }

    private async Task AppendImageContent(List<Microsoft.Extensions.AI.AIContent> contents, ImageContent ic)
    {
        if (blobStorage is not null && !string.IsNullOrEmpty(ic.BlobUri))
        {
            try
            {
                using var stream = await blobStorage.DownloadAsync(ic.BlobUri);
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var imageBytes = memoryStream.ToArray();

                contents.Add(new Microsoft.Extensions.AI.DataContent(imageBytes, ic.MimeType));

                if (!string.IsNullOrEmpty(ic.Caption))
                    contents.Add(new Microsoft.Extensions.AI.TextContent(ic.Caption));

                return;
            }
            catch (Exception)
            {
            }
        }

        contents.Add(new Microsoft.Extensions.AI.TextContent(
            $"[Image: {ic.Caption ?? ic.MimeType}]"));
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