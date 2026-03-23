namespace Core.AI;

public abstract class WhisperModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract int Priority { get; }
    public virtual string Version => "1";
    public virtual string Publisher => "OpenAI";

    private static readonly Lazy<IReadOnlyList<WhisperModel>> _all = new(() =>
        [.. typeof(WhisperModel).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(WhisperModel)) && !t.IsAbstract)
            .Select(t => (WhisperModel)Activator.CreateInstance(t, nonPublic: true)!)
            .OrderByDescending(m => m.Priority)]);

    public static IReadOnlyList<WhisperModel> All => _all.Value;

    public static WhisperModel? FindById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    // kept as no-op for backward compat
    public static void EnsureAllModelsLoaded() { }
}