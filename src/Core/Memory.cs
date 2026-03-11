using Core.Contracts;
using Core.Models;
using ChatMessage = Core.Contracts.ChatMessage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Orleans.Journaling;

namespace IAW.Core;

public abstract class Memory(
    [Memory("agent-state")] IDurableDictionary<string, StateEntry> state,
    [Memory("agent-events")] IDurableList<AgentEvent> eventLog,
    IChatClient chatClient,
    [Memory("history")] IDurableList<ChatMessage> history,
    [Memory("tracking")] IDurableDictionary<string, TrackingItem> trackingItems,
    [Memory("memories")] IDurableList<MemoryEntry> memories,
    IEmbeddingGenerator<string, Embedding<float>> embedder)
    : Agent(state, eventLog, chatClient, history, trackingItems)
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
}
