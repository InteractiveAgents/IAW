namespace Core.AI.Models;

public sealed class MxbaiEmbedLarge : EmbeddingModel
{
    public static readonly MxbaiEmbedLarge Instance = new();
    private MxbaiEmbedLarge() { }

    public override string Id => "mxbai-embed-large";
    public override string DisplayName => "mxbai-embed-large";
    public override string Provider => "ollama";
    public override int Dimensions => 1024;
}
