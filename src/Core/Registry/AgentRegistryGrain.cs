using System.Text;
using Core.Contracts;

namespace Core.Registry;

[GrainType(IAWConstants.GrainTypes.AgentRegistry)]
public class AgentRegistryGrain : Grain, IAgentRegistry
{
    readonly Dictionary<string, AgentRecord> _records = new(StringComparer.OrdinalIgnoreCase);

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
