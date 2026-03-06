using System.Runtime.CompilerServices;
using Core;
using Core.V2;
using Microsoft.Extensions.AI;
using V3Agent = Core.V3.IAgent;

namespace DevUI;

sealed class OrleansAgentChatClient(IClusterClient cluster, ILogger<OrleansAgentChatClient> logger) : IChatClient
{
    // V3 agents registered here get routed via Core.V3.IAgent.GetResponseAsync
    static readonly HashSet<string> V3AgentIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "weather"
    };

    public ChatClientMetadata Metadata { get; } = new("OrleansAgentChatClient");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (agentId, userText) = ExtractAgentAndMessage(chatMessages, options);

        try
        {
            if (V3AgentIds.Contains(agentId))
                return await GetV3ResponseAsync(agentId, userText, cancellationToken);

            return await GetV2ResponseAsync(agentId, userText, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orleans agent {AgentId} call failed", agentId);
            return new ChatResponse(new ChatMessage(
                ChatRole.Assistant,
                $"Agent '{agentId}' could not complete the request: {ex.Message}"));
        }
    }

    async Task<ChatResponse> GetV3ResponseAsync(string agentId, string userText, CancellationToken ct)
    {
        var agent = cluster.GetGrain<V3Agent>(agentId);
        var output = await agent.GetResponseAsync(userText, ct);
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, output));
    }

    async Task<ChatResponse> GetV2ResponseAsync(string agentId, string userText, CancellationToken ct)
    {
        var agent = cluster.GetGrain<IAgent>(agentId, grainClassNamePrefix: "Samples.SmartAgent");
        var reply = await agent.RespondAsync(
            new AgentRequest { Input = userText },
            ct);

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

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (agentId, userText) = ExtractAgentAndMessage(chatMessages, options);

        if (V3AgentIds.Contains(agentId))
        {
            var agent = cluster.GetGrain<V3Agent>(agentId);
            await foreach (var chunk in agent.GetResponse(userText, cancellationToken))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
            yield break;
        }

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
