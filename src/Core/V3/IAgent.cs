namespace Core.V3;

public interface IAgent : IGrainWithStringKey
{
    IAsyncEnumerable<string> GetResponse(string prompt, CancellationToken cancellationToken);
    Task<string> GetResponseAsync(string prompt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task ClearHistoryAsync(CancellationToken cancellationToken = default);
}