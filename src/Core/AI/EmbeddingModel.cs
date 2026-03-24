namespace Core.AI;

public abstract class EmbeddingModel
{
    private readonly string? _id;
    private readonly string? _provider;
    private readonly string? _displayName;
    private readonly int _dimensions;

    public virtual string Id => _id ?? throw new InvalidOperationException(
        "Override Id or use the EmbeddingModel(id, provider, displayName, dimensions) constructor.");
    public virtual string DisplayName => _displayName ?? throw new InvalidOperationException(
        "Override DisplayName or use the constructor.");
    public virtual string Provider => _provider ?? throw new InvalidOperationException(
        "Override Provider or use the constructor.");
    public virtual int Dimensions => _dimensions;

    public bool IsLocal => Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase);

    public string ServiceKey
    {
        get
        {
            var normalizedId = Id.ToLowerInvariant()
                .Replace(".", "")
                .Replace(":", "-");
            return $"{Provider.ToLowerInvariant()}-{normalizedId}";
        }
    }

    private static readonly Lazy<List<EmbeddingModel>> _discovered = new(() =>
        [.. typeof(EmbeddingModel).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(EmbeddingModel)) && !t.IsAbstract && !t.IsNested)
            .Select(t => (EmbeddingModel)Activator.CreateInstance(t, nonPublic: true)!)]);

    private static readonly Lock _lock = new();
    private static readonly List<EmbeddingModel> _runtime = [];

    public static IReadOnlyList<EmbeddingModel> All
    {
        get { lock (_lock) { return [.. _discovered.Value, .. _runtime]; } }
    }

    protected EmbeddingModel() { }

    protected EmbeddingModel(string id, string provider, string displayName, int dimensions)
    {
        _id = id;
        _provider = provider;
        _displayName = displayName;
        _dimensions = dimensions;
    }

    public static EmbeddingModel Register(string id, string provider, string displayName, int dimensions)
    {
        lock (_lock)
        {
            if (_discovered.Value.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                || _runtime.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Embedding model '{id}' is already registered.");
            var model = new RuntimeEmbeddingModel(id, provider, displayName, dimensions);
            _runtime.Add(model);
            return model;
        }
    }

    // no-op — models auto-discover via assembly scanning
    public static void EnsureAllModelsLoaded() { }

    private sealed class RuntimeEmbeddingModel(string id, string provider, string displayName, int dimensions)
        : EmbeddingModel(id, provider, displayName, dimensions);
}