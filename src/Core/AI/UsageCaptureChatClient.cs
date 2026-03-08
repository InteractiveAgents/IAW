using Core.Contracts;
using Microsoft.Extensions.AI;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Core.AI;

// IChatClient wrapper that captures token usage from responses
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

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<AIChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
        => inner.GetStreamingResponseAsync(messages, options, cancellationToken);

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
