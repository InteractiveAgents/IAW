using Core.Models;

namespace Core.Contracts;

public interface IMemoryAgent : IAgent
{
    Task ObserveAsync(string content, string source, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryEntry>> SearchAsync(string query, int topK = 5, CancellationToken ct = default);
    Task ForgetAsync(string content, CancellationToken ct = default);
}
