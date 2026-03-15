namespace Core.AI.Models;

public sealed class GitHubGpt4oMini : LLMModel
{
    public override string Id => "gpt-4o-mini";
    public override string DisplayName => "GitHub GPT-4o Mini";
    public override string Provider => "github";
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}
