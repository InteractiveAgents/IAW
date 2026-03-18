using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Core.Contracts;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using IAgent = Core.Contracts.IAgent;

namespace DevUI;

sealed partial class OrleansAgentChatClient(IClusterClient cluster, ILogger<OrleansAgentChatClient> logger) : IChatClient
{
    // Cache: grain ID → grain interface type (built once from loaded assemblies)
    private static readonly ConcurrentDictionary<string, Type> GrainInterfaceMap = BuildGrainInterfaceMap();

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
        if (GrainInterfaceMap.TryGetValue(agentId, out var interfaceType))
            return (IAgent)cluster.GetGrain(interfaceType, agentId);

        var known = string.Join(", ", GrainInterfaceMap.Keys);
        throw new ArgumentException($"Unknown agent ID: {agentId}. Known: {known}");
    }

    // Build a map of grain ID → grain interface type from loaded assemblies.
    // Uses the same kebab-case convention as AgentDiscovery.
    private static ConcurrentDictionary<string, Type> BuildGrainInterfaceMap()
    {
        var map = new ConcurrentDictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        var agentInterfaces = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .Where(t => t.IsInterface
                         && t != typeof(IAgent)
                         && typeof(IAgent).IsAssignableFrom(t)
                         && !t.IsGenericType);

        foreach (var iface in agentInterfaces)
        {
            var name = iface.Name;
            if (name.StartsWith('I') && name.Length > 1 && char.IsUpper(name[1]))
                name = name[1..];

            var grainId = ToKebabCase(name);
            map.TryAdd(grainId, iface);
        }

        return map;
    }

    private static string ToKebabCase(string pascalCase)
    {
        var kebab = KebabRegex().Replace(pascalCase, "-$1").ToLowerInvariant();
        return kebab.TrimStart('-');
    }

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex KebabRegex();

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
