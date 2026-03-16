using Core.Contracts;
using Orleans.Journaling;

namespace IAW.Agents;

[GrainType("user-profile-v1")]
public class UserProfile(
    [UserProfileState] UserProfileDurableState state)
    : DurableGrain, IUserProfile
{
    public Task<Dictionary<string, string>> GetPreferences(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new Dictionary<string, string>(state.Preferences));
    }

    public async Task SetPreference(string key, string value, CancellationToken ct)
    {
        state.Preferences[key] = value;
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<ProjectInfo>> GetProjects(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<ProjectInfo> projects = [.. state.Projects.Select(kvp => new ProjectInfo(kvp.Key, kvp.Value))];
        return Task.FromResult(projects);
    }

    public async Task RegisterProject(string slug, string topicId, CancellationToken ct)
    {
        state.Projects[slug] = topicId;
        await WriteStateAsync(ct);
    }

    public async Task RemoveProject(string slug, CancellationToken ct)
    {
        state.Projects.Remove(slug);
        await WriteStateAsync(ct);
    }

    public Task<string?> ResolveProject(string topicId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var kvp in state.Projects)
        {
            if (kvp.Value == topicId)
                return Task.FromResult<string?>(kvp.Key);
        }
        return Task.FromResult<string?>(null);
    }

    public async Task RememberFact(string fact, CancellationToken ct)
    {
        // store facts as preferences with a "fact:" prefix key
        var factKey = $"fact:{Guid.NewGuid():N}";
        state.Preferences[factKey] = fact;
        await WriteStateAsync(ct);
    }

    public Task<IReadOnlyList<string>> RecallFacts(string query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<string> facts = [.. state.Preferences
            .Where(kvp => kvp.Key.StartsWith("fact:"))
            .Select(kvp => kvp.Value)
            .Where(v => v.Contains(query, StringComparison.OrdinalIgnoreCase))];
        return Task.FromResult(facts);
    }
}
