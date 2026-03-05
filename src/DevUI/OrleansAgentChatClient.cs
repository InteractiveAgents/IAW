using System.Runtime.CompilerServices;
using Core;
using Core.V2;
using Microsoft.Extensions.AI;

namespace DevUI;

sealed class OrleansAgentChatClient(IClusterClient cluster, ILogger<OrleansAgentChatClient> logger) : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("OrleansAgentChatClient");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (agentId, userText) = ExtractAgentAndMessage(chatMessages, options);

        try
        {
            var agent = cluster.GetGrain<IAgent>(agentId, grainClassNamePrefix: "Samples.SmartAgent");
            var reply = await agent.RespondAsync(
                new AgentRequest { Input = userText },
                cancellationToken);

            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, reply.Output))
            {
                ModelId = reply.ModelId
            };

            if (reply.InputTokens.HasValue || reply.OutputTokens.HasValue || reply.TotalTokens.HasValue)
            {
                response.Usage = new UsageDetails
                {
                    InputTokenCount = reply.InputTokens,
                    OutputTokenCount = reply.OutputTokens,
                    TotalTokenCount = reply.TotalTokens
                };
            }

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orleans agent {AgentId} call failed", agentId);
            return new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                $"Agent '{agentId}' could not complete the request: {ex.Message}"));
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(chatMessages, options, cancellationToken);
        var text = response.Messages.FirstOrDefault()?.Text ?? string.Empty;
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    static (string AgentId, string UserText) ExtractAgentAndMessage(
        IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var agentId = options?.Instructions?.Trim();

        if (string.IsNullOrEmpty(agentId))
        {
            var messageList = messages.ToList();
            var systemMsg = messageList.FirstOrDefault(m => m.Role == ChatRole.System);
            agentId = systemMsg?.Text?.Trim();

            if (string.IsNullOrEmpty(agentId))
                throw new InvalidOperationException(
                    "Cannot determine agent ID — no Instructions or system message provided.");

            var userMsg = messageList.LastOrDefault(m => m.Role == ChatRole.User);
            return (agentId, userMsg?.Text ?? string.Empty);
        }

        var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
        return (agentId, userMessage?.Text ?? string.Empty);
    }
}
