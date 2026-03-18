using Core.Contracts;
using Core.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;
using IAW.Core;

namespace Core;

public abstract class MemoryAgentBase(
    [AgentState] AgentDurableState durableState,
    IChatClient chat,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder,
    ILogger logger)
    : Agent(durableState, chat), IMemoryAgent
{
    protected IDurableList<MemoryEntry> Memories => memories;
    protected IEmbeddingGenerator<string, Embedding<float>> Embedder => embedder;

    private const int MaxMemories = 500;

    protected abstract string CollectionName { get; }

    protected override string Instructions =>
        $"You are {DisplayName}, an IAW team memory agent. You observe, store, search, and consolidate knowledge. " +
        "When asked to store, store immediately. When asked to recall, search and return results.";

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

        // Apply size guardrail: remove lowest relevance entry if at capacity
        if (memories.Count >= MaxMemories)
        {
            var lowestIdx = 0;
            var lowestScore = memories[0].RelevanceScore;
            for (var i = 1; i < memories.Count; i++)
            {
                if (memories[i].RelevanceScore < lowestScore)
                {
                    lowestScore = memories[i].RelevanceScore;
                    lowestIdx = i;
                }
            }
            memories.RemoveAt(lowestIdx);
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

        IReadOnlyList<MemoryEntry> topResults = [.. scored
            .OrderByDescending(s => s.Score)
            .Take(topK)
            .Select(s => s.Entry)];
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

    protected virtual async Task Consolidate(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (memories.Count < 20)
            return;

        var clusters = FindSimilarClusters(memories, 0.85f);
        var consolidatedEntries = new List<MemoryEntry>();
        var indicesToRemove = new HashSet<int>();

        foreach (var cluster in clusters.Where(c => c.Count >= 3))
        {
            var indices = cluster.Select(x => x.Index).ToList();
            var entries = cluster.Select(x => x.Entry).ToList();
            var contents = entries.Select(e => e.Content).ToList();

            try
            {
                var consolidatedContent = await SummarizeCluster(contents, ct);
                var consolidated = new MemoryEntry(
                    Guid.NewGuid().ToString("N"),
                    consolidatedContent,
                    entries[0].Source,
                    entries.Average(e => e.RelevanceScore),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    entries.Sum(e => e.AccessCount),
                    null);

                if (entries[0].Embedding is not null)
                {
                    try
                    {
                        var result = await Embedder.GenerateAsync([consolidatedContent], cancellationToken: ct);
                        consolidated = consolidated with { Embedding = result[0].Vector.ToArray() };
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Embedding generation failed for consolidated entry");
                    }
                }

                consolidatedEntries.Add(consolidated);
                foreach (var idx in indices)
                    indicesToRemove.Add(idx);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Consolidation of cluster failed, keeping originals");
            }
        }

        // Remove original entries (in reverse order to preserve indices)
        foreach (var idx in indicesToRemove.OrderByDescending(x => x))
            memories.RemoveAt(idx);

        // Add consolidated entries
        foreach (var entry in consolidatedEntries)
            memories.Add(entry);

        await WriteStateAsync(ct);
    }

    private static List<List<(int Index, MemoryEntry Entry)>> FindSimilarClusters(IDurableList<MemoryEntry> memories, float threshold)
    {
        var clusters = new List<List<(int, MemoryEntry)>>();
        var visited = new HashSet<int>();

        for (var i = 0; i < memories.Count; i++)
        {
            if (visited.Contains(i) || memories[i].Embedding is null)
                continue;

            var cluster = new List<(int, MemoryEntry)> { (i, memories[i]) };
            visited.Add(i);

            for (var j = i + 1; j < memories.Count; j++)
            {
                if (!visited.Contains(j) && memories[j].Embedding is not null)
                {
                    var similarity = CosineSimilarity(memories[i].Embedding!, memories[j].Embedding!);
                    if (similarity > threshold)
                    {
                        cluster.Add((j, memories[j]));
                        visited.Add(j);
                    }
                }
            }

            if (cluster.Count > 0)
                clusters.Add(cluster);
        }

        return clusters;
    }

    private async Task<string> SummarizeCluster(List<string> contents, CancellationToken ct)
    {
        var contentsText = string.Join("\n---\n", contents);
        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.User, $"Consolidate these related memory entries into a single concise entry:\n\n{contentsText}")
        };
        var response = await ChatClient.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text ?? "consolidated memory";
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
