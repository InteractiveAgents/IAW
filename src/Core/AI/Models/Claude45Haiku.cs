namespace IAW.Core.AI.Models;

public sealed class Claude45Haiku : LLMModel
{
    public static readonly Claude45Haiku Instance = new();
    private Claude45Haiku() { }

    public override string Id => "claude-haiku-4-5-20251001";
    public override string DisplayName => "Claude 4.5 Haiku";
    public override ProviderType Provider => ProviderType.Anthropic;
    public override ModelCapabilities Capabilities => ModelCapabilities.FullyCapable;
}
