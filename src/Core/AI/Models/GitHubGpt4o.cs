namespace Core.AI.Models;

public sealed class GitHubGpt4o : LLMModel
{
    public static readonly GitHubGpt4o Instance = new();
    private GitHubGpt4o() { }

    public override string Id => "gpt-4o";
    public override string DisplayName => "GitHub GPT-4o";
    public override ProviderType Provider => ProviderType.GitHub;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
