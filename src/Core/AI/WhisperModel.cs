namespace Core.AI;

public abstract class WhisperModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract int Priority { get; }
    public virtual string Version => "1";
    public virtual string Publisher => "OpenAI";

    private static readonly List<WhisperModel> _registry = [];
    private static readonly Lock _lock = new();

    public static IReadOnlyList<WhisperModel> All
    {
        get { lock (_lock) { return [.. _registry]; } }
    }

    protected WhisperModel()
    {
        lock (_lock) { _registry.Add(this); }
    }

    public static WhisperModel? FindById(string id) =>
        All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public static void EnsureAllModelsLoaded()
    {
        _ = Models.WhisperLargeV3Turbo.Instance;
        _ = Models.WhisperSmall.Instance;
        _ = Models.WhisperTiny.Instance;
    }
}
