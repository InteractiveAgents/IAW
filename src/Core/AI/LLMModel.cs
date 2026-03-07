namespace IAW.Core.AI;

public abstract class LLMModel
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract ProviderType Provider { get; }
    public abstract ModelCapabilities Capabilities { get; }

    public bool IsLocal => Provider == ProviderType.Ollama;

    public string ServiceKey
    {
        get
        {
            var normalizedId = Id.ToLowerInvariant()
                .Replace(".", "")
                .Replace(":", "-");
            return $"{Provider.ToString().ToLowerInvariant()}-{normalizedId}";
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

    public static void EnsureAllModelsLoaded()
    {
        _ = Models.Claude45Haiku.Instance;
        _ = Models.Sonnet46.Instance;
        _ = Models.Gpt4o.Instance;
        _ = Models.Gpt4oMini.Instance;
        _ = Models.Llama32.Instance;
        _ = Models.Qwen25.Instance;
        _ = Models.GitHubGpt4oMini.Instance;
        _ = Models.GitHubGpt4o.Instance;
    }
}
