namespace Core.AI.Models.GitHub;

public sealed class Gpt41Mini : LLMModel
{
    public override string Id => "openai/gpt-4.1-mini";
    public override string DisplayName => "GitHub GPT-4.1 Mini";
    public override string Provider => "github";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
