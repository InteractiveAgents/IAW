namespace Core.AI.Models.GitHub;

public sealed class Gpt4oMini : LLMModel
{
    public override string Id => "openai/gpt-4o-mini";
    public override string DisplayName => "GitHub GPT-4o Mini";
    public override string Provider => "github";
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}
