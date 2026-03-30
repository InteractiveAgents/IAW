namespace Core.AI.Models.GitHub;

public sealed class Gpt41Nano : LLMModel
{
    public override string Id => "openai/gpt-4.1-nano";
    public override string DisplayName => "GitHub GPT-4.1 Nano";
    public override string Provider => "github";
    public override ModelCapabilities Capabilities => ModelCapabilities.ToolCapable;
}
