using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using IAW.Core;

namespace Core;

public abstract class Memory(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger logger)
    : Agent(durableState, chatClient), IMemoryAgent
{
    protected IDurableList<MemoryEntry> Memories => memories;
    protected IEmbeddingGenerator<string, Embedding<float>> Embedder => embedder;

    protected abstract string CollectionName { get; }

    protected override string Instructions =>
        $"You are {DisplayName}, a memory agent. You observe, store, search, and consolidate knowledge.";

    protected virtual async Task Observe(string content, MemoryProvenance provenance, CancellationToken ct = default)
    {
        float[]? embedding = null;
        try
        {
            var result = await Embedder.GenerateAsync([content], cancellationToken: ct);
            embedding = result[0].Vector.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding generation failed during Observe for {AgentId}, storing without embedding",
                this.GetPrimaryKeyString());
        }

        var entry = new MemoryEntry(
            Guid.NewGuid().ToString("N"),
            content, provenance, 1.0f,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, null)
        { Embedding = embedding };

        memories.Add(entry);
        await WriteStateAsync(ct);
    }

    protected virtual async Task<IReadOnlyList<MemoryEntry>> Search(string query, int topK = 5, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        float[]? queryEmbedding = null;
        try
        {
            var result = await Embedder.GenerateAsync([query], cancellationToken: ct);
            queryEmbedding = result[0].Vector.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedding generation failed during Search for {AgentId}, using keyword fallback",
                this.GetPrimaryKeyString());
        }

        var scored = new List<(MemoryEntry Entry, float Score)>();
        for (var i = 0; i < memories.Count; i++)
        {
            var entry = memories[i];
            float score;

            if (queryEmbedding is not null && entry.Embedding is not null)
                score = CosineSimilarity(queryEmbedding, entry.Embedding);
            else if (entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                score = 0.5f;
            else
                continue;

            var updated = entry with { AccessCount = entry.AccessCount + 1, LastAccessedAt = DateTimeOffset.UtcNow };
            memories[i] = updated;
            scored.Add((updated, score * entry.RelevanceScore));
        }

        if (scored.Count > 0)
            await WriteStateAsync(ct);

        IReadOnlyList<MemoryEntry> topResults = scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => s.Entry)
            .ToList();
        return topResults;
    }

    static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0, magA = 0, magB = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        var denom = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return denom == 0 ? 0f : dot / denom;
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
