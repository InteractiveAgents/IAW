namespace IAW.Core.AI.Models;

public sealed class GitHubGpt4oMini : LLMModel
{
    public static readonly GitHubGpt4oMini Instance = new();
    private GitHubGpt4oMini() { }

    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GitHub GPT-4o Mini";
    public override ProviderType Provider => ProviderType.GitHub;
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}
