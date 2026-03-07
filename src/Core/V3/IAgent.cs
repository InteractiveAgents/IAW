namespace Core.V3;

public interface IAgent : IGrainWithStringKey
{
    IAsyncEnumerable<string> GetResponseStream(string prompt, CancellationToken cancellationToken);
    Task<string> GetResponse(string prompt, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> GetHistory(CancellationToken cancellationToken);
    Task ClearHistoryAsync(CancellationToken cancellationToken);
}