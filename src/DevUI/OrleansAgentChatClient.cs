using System.Runtime.CompilerServices;
using Core;
using Core.Contracts;
using IAW.Agents.Orchestration;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using IAgent = Core.Contracts.IAgent;

namespace DevUI;

sealed class OrleansAgentChatClient(IClusterClient cluster, ILogger<OrleansAgentChatClient> logger) : IChatClient
{
    private readonly string _devuiThreadId = $"devui/{Guid.NewGuid().ToString("N")[..8]}";

    public ChatClientMetadata Metadata { get; } = new("OrleansAgentChatClient");

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (agentId, userText) = ExtractAgentAndMessage(chatMessages, options);

        try
        {
            var agent = ResolveAgent(agentId);
            var output = await agent.GetResponse(userText, cancellationToken);
            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, output));

            var usage = await agent.GetLastUsage(cancellationToken);
            if (usage is not null)
            {
                response.Usage = new UsageDetails
                {
                    InputTokenCount = usage.InputTokens,
                    OutputTokenCount = usage.OutputTokens,
                    TotalTokenCount = usage.TotalTokens
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
        var (agentId, userText) = ExtractAgentAndMessage(chatMessages, options);

        var agent = ResolveAgent(agentId);
        await foreach (var chunk in agent.GetResponseStream(userText, cancellationToken))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }

    private IAgent ResolveAgent(string agentId)
    {
        if (IsThreadAgent(agentId))
            return (IAgent)cluster.GetGrain(typeof(IThread), _devuiThreadId);

        var interfaceType = AgentInterfaceResolver.Resolve(agentId);
        if (interfaceType is not null)
            return (IAgent)cluster.GetGrain(interfaceType, agentId);

        var known = string.Join(", ",
            AgentInterfaceResolver.DiscoverAgentInterfaces().Select(t => t.Name.TrimStart('I').ToLowerInvariant()));
        throw new ArgumentException($"Unknown agent ID: {agentId}. Known: {known}");
    }

    private static bool IsThreadAgent(string agentId) =>
        string.Equals(agentId, "thread", StringComparison.OrdinalIgnoreCase);

    // First line of instructions = agent grain ID for routing.
    static (string AgentId, string UserText) ExtractAgentAndMessage(
        IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var raw = options?.Instructions?.Trim();

        if (!string.IsNullOrEmpty(raw))
        {
            var agentId = raw.Contains('\n') ? raw[..raw.IndexOf('\n')].Trim() : raw;
            var userMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
            return (agentId, userMessage?.Text ?? string.Empty);
        }

        var messageList = messages.ToList();
        var systemMsg = messageList.FirstOrDefault(m => m.Role == ChatRole.System);
        var sysText = systemMsg?.Text?.Trim();

        if (string.IsNullOrEmpty(sysText))
            throw new InvalidOperationException(
                "Cannot determine agent ID — no Instructions or system message provided.");

        var sysAgentId = sysText.Contains('\n') ? sysText[..sysText.IndexOf('\n')].Trim() : sysText;
        var userMsg = messageList.LastOrDefault(m => m.Role == ChatRole.User);
        return (sysAgentId, userMsg?.Text ?? string.Empty);
    }
}
