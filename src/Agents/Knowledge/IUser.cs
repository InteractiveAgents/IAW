using IAW.Core;

namespace IAW.Agents.Knowledge;

public interface IUser : IAgent
{
    Task<string> GetPreferenceAsync(string key, CancellationToken ct = default);
    Task SetPreferenceAsync(string key, string value, CancellationToken ct = default);
    Task AddMemoryAsync(string memory, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetMemoriesAsync(CancellationToken ct = default);
}
