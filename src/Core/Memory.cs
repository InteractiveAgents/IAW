using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Orleans.Journaling;
using IAW.Core;

namespace Core;

public abstract class Memory(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : Agent(durableState, chatClient), IMemoryAgent
{
    protected IDurableList<MemoryEntry> Memories => memories;
    protected IEmbeddingGenerator<string, Embedding<float>> Embedder => embedder;

    protected abstract string CollectionName { get; }

    protected override string Instructions =>
        $"You are {DisplayName}, a memory agent. You observe, store, search, and consolidate knowledge.";

    protected virtual async Task Observe(string content, MemoryProvenance provenance, CancellationToken ct = default)
    {
        var entry = new MemoryEntry(
            Guid.NewGuid().ToString("N"),
            content, provenance, 1.0f,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null);
        memories.Add(entry);
        await WriteStateAsync(ct);
    }

    protected virtual Task<IReadOnlyList<MemoryEntry>> Search(string query, int topK = 5, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var results = new List<MemoryEntry>();
        foreach (var entry in memories)
        {
            if (entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                results.Add(entry with { AccessCount = entry.AccessCount + 1, LastAccessedAt = DateTimeOffset.UtcNow });
        }
        IReadOnlyList<MemoryEntry> topResults = results.OrderByDescending(e => e.RelevanceScore).Take(topK).ToList();
        return Task.FromResult(topResults);
    }

    protected virtual Task Consolidate(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    protected virtual async Task Decay(float decayFactor = 0.95f, CancellationToken ct = default)
    {
        for (var i = 0; i < memories.Count; i++)
        {
            var entry = memories[i];
            memories[i] = entry with { RelevanceScore = entry.RelevanceScore * decayFactor };
        }
        await WriteStateAsync(ct);
    }

    protected virtual async Task Forget(string memoryId, CancellationToken ct = default)
    {
        for (var i = 0; i < memories.Count; i++)
        {
            if (memories[i].Id == memoryId)
            {
                memories.RemoveAt(i);
                await WriteStateAsync(ct);
                return;
            }
        }
    }

    // IMemoryAgent public interface — delegates to protected methods
    public Task ObserveAsync(string content, string source, CancellationToken ct = default)
    {
        var provenance = new MemoryProvenance(
            source, null, this.GetPrimaryKeyString(), null,
            DateTimeOffset.UtcNow, null, 1.0f);
        return Observe(content, provenance, ct);
    }

    public Task<IReadOnlyList<MemoryEntry>> SearchAsync(string query, int topK = 5, CancellationToken ct = default)
        => Search(query, topK, ct);

    public async Task ForgetAsync(string content, CancellationToken ct = default)
    {
        for (var i = 0; i < memories.Count; i++)
        {
            if (memories[i].Content.Equals(content, StringComparison.OrdinalIgnoreCase))
            {
                memories.RemoveAt(i);
                await WriteStateAsync(ct);
                return;
            }
        }
    }
}
