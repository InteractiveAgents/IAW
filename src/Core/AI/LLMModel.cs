namespace Core.AI;

public abstract class LLMModel
{
    private readonly string? _id;
    private readonly string? _provider;
    private readonly string? _displayName;
    private readonly ModelCapabilities? _capabilities;

    public virtual string Id => _id ?? throw new InvalidOperationException(
        "Override Id or use the LLMModel(id, provider, displayName) constructor.");
    public virtual string DisplayName => _displayName ?? throw new InvalidOperationException(
        "Override DisplayName or use the LLMModel(id, provider, displayName) constructor.");
    public virtual string Provider => _provider ?? throw new InvalidOperationException(
        "Override Provider or use the LLMModel(id, provider, displayName) constructor.");
    public virtual ModelCapabilities Capabilities => _capabilities ?? ModelCapabilities.FullyCapable;

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

    private static readonly Lazy<List<LLMModel>> _discovered = new(() =>
        [.. typeof(LLMModel).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(LLMModel)) && !t.IsAbstract && !t.IsNested)
            .Select(t => (LLMModel)Activator.CreateInstance(t, nonPublic: true)!)]);

    private static readonly Lock _lock = new();
    private static readonly List<LLMModel> _runtime = [];

    public static IReadOnlyList<LLMModel> All
    {
        get { lock (_lock) { return [.. _discovered.Value, .. _runtime]; } }
    }

    protected LLMModel() { }

    protected LLMModel(string id, string provider, string displayName, ModelCapabilities? capabilities = null)
    {
        _id = id;
        _provider = provider;
        _displayName = displayName;
        _capabilities = capabilities;
    }

    public static LLMModel Register(string id, string provider, string displayName, ModelCapabilities? capabilities = null)
    {
        lock (_lock)
        {
            if (_discovered.Value.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                || _runtime.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Model '{id}' is already registered.");
            var model = new RuntimeLLMModel(id, provider, displayName, capabilities ?? ModelCapabilities.FullyCapable);
            _runtime.Add(model);
            return model;
        }
    }

    // no-op — models auto-discover via assembly scanning
    public static void EnsureAllModelsLoaded() { }

    private sealed class RuntimeLLMModel(string id, string provider, string displayName, ModelCapabilities capabilities)
        : LLMModel(id, provider, displayName, capabilities);
}