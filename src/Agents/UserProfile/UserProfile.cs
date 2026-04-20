using Core;
using Core.Contracts;
using Orleans.Journaling;

namespace IAW.Agents;

[GrainType(IAWConstants.GrainTypes.UserProfile)]
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
        IReadOnlyList<ProjectInfo> projects = state.Projects.Select(kvp => new ProjectInfo(kvp.Key, kvp.Value)).ToArray();
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

    public Task<int?> GetTopicId(string slug, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (state.Projects.TryGetValue(slug, out var value) && int.TryParse(value, out var topicId))
            return Task.FromResult<int?>(topicId);
        return Task.FromResult<int?>(null);
    }

    public async Task SetTopicId(string slug, int topicId, CancellationToken ct)
    {
        state.Projects[slug] = topicId.ToString();
        await WriteStateAsync(ct);
    }
}