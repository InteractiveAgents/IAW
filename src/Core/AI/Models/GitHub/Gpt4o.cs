namespace Core.AI.Models.GitHub;

public sealed class Gpt4o : LLMModel
{
    public override string Id => "openai/gpt-4o";
    public override string DisplayName => "GitHub GPT-4o";
    public override string Provider => "github";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
