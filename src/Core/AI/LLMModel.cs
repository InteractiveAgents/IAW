namespace Core.AI;

public abstract class LLMModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Provider { get; }
    public abstract ModelCapabilities Capabilities { get; }

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

    public static LLMModel Register(string id, string provider, string displayName, ModelCapabilities? capabilities = null)
    {
        lock (_lock)
        {
            if (_registry.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"Model '{id}' is already registered.");
        }
        return new ConfiguredLLMModel(id, provider, displayName, capabilities ?? ModelCapabilities.FullyCapable);
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

    private sealed class ConfiguredLLMModel(string id, string provider, string displayName, ModelCapabilities capabilities) : LLMModel
    {
        public override string Id => id;
        public override string Provider => provider;
        public override string DisplayName => displayName;
        public override ModelCapabilities Capabilities => capabilities;
    }
}
