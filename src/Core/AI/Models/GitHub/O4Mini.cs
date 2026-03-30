namespace Core.AI.Models.GitHub;

public sealed class O4Mini : LLMModel
{
    public override string Id => "openai/o4-mini";
    public override string DisplayName => "GitHub o4-mini";
    public override string Provider => "github";
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
