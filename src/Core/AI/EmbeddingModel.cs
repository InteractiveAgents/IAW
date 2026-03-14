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

    private static readonly List<EmbeddingModel> _registry = [];
    private static readonly Lock _lock = new();

    public static IReadOnlyList<EmbeddingModel> All
    {
        get { lock (_lock) { return [.. _registry]; } }
    }

    protected EmbeddingModel()
    {
        lock (_lock) { _registry.Add(this); }
    }

    protected EmbeddingModel(string id, string provider, string displayName, int dimensions)
    {
        _id = id;
        _provider = provider;
        _displayName = displayName;
        _dimensions = dimensions;
        lock (_lock) { _registry.Add(this); }
    }

    public static EmbeddingModel Register(string id, string provider, string displayName, int dimensions)
    {
        lock (_lock)
        {
            if (_registry.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Embedding model '{id}' is already registered.");
        }
        return new RuntimeEmbeddingModel(id, provider, displayName, dimensions);
    }

    public static void EnsureAllModelsLoaded()
    {
        _ = Models.MxbaiEmbedLarge.Instance;
        _ = Models.TextEmbedding3Small.Instance;
    }

    private sealed class RuntimeEmbeddingModel(string id, string provider, string displayName, int dimensions)
        : EmbeddingModel(id, provider, displayName, dimensions);
}
