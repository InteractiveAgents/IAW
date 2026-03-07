namespace Core.V3.Context;

public interface IAIContextProvider
{
    Task<AIContext> ProvideContextAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);
    Task StoreContextAsync(IReadOnlyList<ChatMessage> request, AgentResponse response, CancellationToken ct = default);
}
