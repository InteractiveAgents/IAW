namespace Core.Contracts;

public interface IUserProfile : IGrainWithStringKey
{
    Task<Dictionary<string, string>> GetPreferences(CancellationToken ct);
    Task SetPreference(string key, string value, CancellationToken ct);
    Task<IReadOnlyList<ProjectInfo>> GetProjects(CancellationToken ct);
    Task RegisterProject(string slug, string topicId, CancellationToken ct);
    Task RemoveProject(string slug, CancellationToken ct);
    Task<string?> ResolveProject(string topicId, CancellationToken ct);
    Task RememberFact(string fact, CancellationToken ct);
    Task<IReadOnlyList<string>> RecallFacts(string query, CancellationToken ct);
    Task<int?> GetTopicId(string slug, CancellationToken ct);
    Task SetTopicId(string slug, int topicId, CancellationToken ct);
}
