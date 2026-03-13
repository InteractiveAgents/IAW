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

    private static readonly List<LLMModel> _registry = [];
    private static readonly Lock _lock = new();

    public static IReadOnlyList<LLMModel> All
    {
        get { lock (_lock) { return [.. _registry]; } }
    }

    protected LLMModel()
    {
        lock (_lock) { _registry.Add(this); }
    }

    protected LLMModel(string id, string provider, string displayName, ModelCapabilities? capabilities = null)
    {
        _id = id;
        _provider = provider;
        _displayName = displayName;
        _capabilities = capabilities;
        lock (_lock) { _registry.Add(this); }
    }

    public static LLMModel Register(string id, string provider, string displayName, ModelCapabilities? capabilities = null)
    {
        lock (_lock)
        {
            if (_registry.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Model '{id}' is already registered.");
        }
        return new RuntimeLLMModel(id, provider, displayName, capabilities ?? ModelCapabilities.FullyCapable);
    }

    public static void EnsureAllModelsLoaded()
    {
        _ = Models.Claude45Haiku.Instance;
        _ = Models.Sonnet46.Instance;
        _ = Models.Opus46.Instance;
        _ = Models.Gpt4o.Instance;
        _ = Models.Gpt4oMini.Instance;
        _ = Models.Gpt52.Instance;
        _ = Models.Gpt53.Instance;
        _ = Models.Gemini31.Instance;
        _ = Models.GrokLatest.Instance;
        _ = Models.Llama32.Instance;
        _ = Models.Qwen25.Instance;
        _ = Models.GitHubGpt4oMini.Instance;
        _ = Models.GitHubGpt4o.Instance;
    }

    // Runtime-only model — for programmatic registration without [Llm<T>].
    // For [Llm<T>] support, define your own class:
    //   public sealed class MyModel : LLMModel
    //   {
    //       public static readonly MyModel Instance = new();
    //       private MyModel() : base("my-model", "openai", "My Model") { }
    //   }
    private sealed class RuntimeLLMModel(string id, string provider, string displayName, ModelCapabilities capabilities)
        : LLMModel(id, provider, displayName, capabilities);
}
