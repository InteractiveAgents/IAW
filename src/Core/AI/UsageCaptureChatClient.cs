using System.Runtime.CompilerServices;
using Core.Contracts;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Core.AI;

// IChatClient wrapper that captures token usage from both regular and streaming responses
internal sealed class UsageCaptureChatClient(IChatClient inner) : IChatClient
{
    private volatile AgentUsage? _lastUsage;

    public AgentUsage? LastUsage => _lastUsage;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<AIChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        var response = await inner.GetResponseAsync(messages, options, cancellationToken);
        CaptureUsage(response.Usage);
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AIChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        await foreach (var update in inner.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usageContent)
                {
                    inputTokens += usageContent.Details.InputTokenCount ?? 0;
                    outputTokens += usageContent.Details.OutputTokenCount ?? 0;
                    totalTokens += usageContent.Details.TotalTokenCount ?? 0;
                }
            }
            yield return update;
        }

        if (inputTokens > 0 || outputTokens > 0)
            _lastUsage = new AgentUsage(inputTokens, outputTokens, totalTokens);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(UsageCaptureChatClient) ? this : inner.GetService(serviceType, serviceKey);

    public void Dispose() { }

    private void CaptureUsage(UsageDetails? usage)
    {
        if (usage is null) return;

        _lastUsage = new AgentUsage(
            usage.InputTokenCount ?? 0,
            usage.OutputTokenCount ?? 0,
            usage.TotalTokenCount ?? 0);
    }
}
