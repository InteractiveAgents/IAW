using System.Text;

namespace Core.Registry;

[GrainType(IAWConstants.GrainTypes.AgentRegistry)]
public class AgentRegistryGrain : Grain, IAgentRegistry
{
    readonly Dictionary<string, AgentRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (_records.Count == 0)
        {
            foreach (var record in AgentRegistrationStartupTask.DiscoverAndBuildRecords())
                _records[record.AgentType] = record;
        }
        return base.OnActivateAsync(cancellationToken);
    }

    public Task RegisterAsync(AgentRecord record, CancellationToken ct = default)
    {
        _records[record.AgentType] = record;
        return Task.CompletedTask;
    }

    public Task<List<AgentCandidate>> SearchAsync(string query, string? namespaceFilter = null, int top = 15, CancellationToken ct = default)
    {
        var queryTerms = query
            .Split([' ', ',', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

        var candidates = _records.Values
            .Where(r => namespaceFilter is null || r.Namespace.Equals(namespaceFilter, StringComparison.OrdinalIgnoreCase))
            .Select(r => ScoreRecord(r, queryTerms))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .Take(top)
            .ToList();

        return Task.FromResult(candidates);
    }

    public Task<List<AgentRecord>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(_records.Values.ToList());

    public Task<string> ToPromptStringAsync(CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Agent Catalog");
        sb.AppendLine();

        var grouped = _records.Values
            .GroupBy(r => r.Namespace)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            foreach (var record in group.OrderBy(r => r.InterfaceName))
            {
                sb.Append($"- **{record.InterfaceName}** — {record.Description}");
                if (record.Capabilities.Length > 0)
                    sb.Append($" [{string.Join(", ", record.Capabilities)}]");
                sb.AppendLine();
            }
            sb.AppendLine();
        }

        return Task.FromResult(sb.ToString());
    }

    public Task<List<AgentCandidate>> HybridSearchAsync(string query, ReadOnlyMemory<float> queryEmbedding, string? namespaceFilter = null, int top = 5, CancellationToken ct = default)
    {
        var queryTerms = query
            .Split([' ', ',', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

        // detect if query embedding is real (non-zero) vs NoOp zeros
        var hasRealEmbedding = queryEmbedding.Length > 0 && !IsZeroVector(queryEmbedding.Span);

        var candidates = _records.Values
            .Where(r => namespaceFilter is null || r.Namespace.Equals(namespaceFilter, StringComparison.OrdinalIgnoreCase))
            .Select(r =>
            {
                var keywordCandidate = ScoreRecord(r, queryTerms);
                var keywordScore = keywordCandidate.Score;

                var vectorScore = hasRealEmbedding && r.DescriptionEmbedding.Length > 0 && !IsZeroVector(r.DescriptionEmbedding.Span)
                    ? CosineSimilarity(queryEmbedding.Span, r.DescriptionEmbedding.Span)
                    : 0f;

                var combined = hasRealEmbedding && vectorScore > 0f
                    ? 0.6f * vectorScore + 0.4f * keywordScore
                    : keywordScore;

                return keywordCandidate with { Score = combined };
            })
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .Take(top)
            .ToList();

        return Task.FromResult(candidates);
    }

    static bool IsZeroVector(ReadOnlySpan<float> v)
    {
        for (var i = 0; i < v.Length; i++)
            if (v[i] != 0f) return false;
        return true;
    }

    static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        for (var i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0f ? dot / denom : 0f;
    }

    public Task<AgentRecord?> GetByAgentTypeAsync(string agentType, CancellationToken ct = default)
        => Task.FromResult(_records.TryGetValue(agentType, out var record) ? record : null);

    static AgentCandidate ScoreRecord(AgentRecord record, HashSet<string> queryTerms)
    {
        var searchText = $"{record.Description} {string.Join(" ", record.Capabilities)} {record.DisplayName} {record.InterfaceName} {record.AgentType}"
            .ToLowerInvariant();

        var matchCount = queryTerms.Count(term => searchText.Contains(term, StringComparison.Ordinal));
        var score = queryTerms.Count > 0 ? (float)matchCount / queryTerms.Count : 0f;

        return new AgentCandidate(
            record.AgentType,
            record.Namespace,
            record.DisplayName,
            record.Description,
            record.InterfaceName,
            score);
    }
}