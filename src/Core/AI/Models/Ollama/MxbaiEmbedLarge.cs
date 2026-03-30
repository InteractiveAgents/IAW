namespace Core.AI.Models.Ollama;

public sealed class MxbaiEmbedLarge : EmbeddingModel
{
    public override string Id => "mxbai-embed-large";
    public override string DisplayName => "mxbai-embed-large";
    public override string Provider => "ollama";
    public override int Dimensions => 1024;
}
