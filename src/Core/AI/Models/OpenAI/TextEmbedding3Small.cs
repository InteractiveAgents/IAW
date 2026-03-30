namespace Core.AI.Models.OpenAI;

public sealed class TextEmbedding3Small : EmbeddingModel
{
    public override string Id => "text-embedding-3-small";
    public override string DisplayName => "Text Embedding 3 Small";
    public override string Provider => "openai";
    public override int Dimensions => 1536;
}
