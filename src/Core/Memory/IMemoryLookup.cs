namespace Core.Memory;

public interface IMemoryLookup
{
    Task<MemoryHit?> LookupOriginAsync(string userId, string question, CancellationToken ct);
}
