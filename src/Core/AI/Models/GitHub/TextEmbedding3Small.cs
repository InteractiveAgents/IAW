namespace Core.AI.Models.GitHub;

public sealed class TextEmbedding3Small : EmbeddingModel
{
    public override string Id => "openai/text-embedding-3-small";
    public override string DisplayName => "GitHub Text Embedding 3 Small";
    public override string Provider => "github";
    public override int Dimensions => 1536;
}
