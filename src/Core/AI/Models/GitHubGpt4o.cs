namespace Core.AI.Models;

public sealed class GitHubGpt4o : LLMModel
{
    public override string Id => "gpt-4o";
    public override string DisplayName => "GitHub GPT-4o";
    public override string Provider => "github";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}