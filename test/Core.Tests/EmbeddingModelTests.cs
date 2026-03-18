using Core.AI;
using Xunit;

namespace IAW.Core.Tests;

public class EmbeddingModelTests
{
    [Fact]
    public void MxbaiEmbedLarge_RegistersInRegistry()
    {
        EmbeddingModel.EnsureAllModelsLoaded();
        var model = EmbeddingModel.All.FirstOrDefault(m => m.Id == "mxbai-embed-large");
        Assert.NotNull(model);
        Assert.Equal(1024, model.Dimensions);
        Assert.Equal("ollama", model.Provider);
    }

    [Fact]
    public void TextEmbedding3Small_RegistersInRegistry()
    {
        EmbeddingModel.EnsureAllModelsLoaded();
        var model = EmbeddingModel.All.FirstOrDefault(m => m.Id == "text-embedding-3-small");
        Assert.NotNull(model);
        Assert.Equal(1536, model.Dimensions);
        Assert.Equal("openai", model.Provider);
    }

    [Fact]
    public void ServiceKey_MatchesLLMModelFormula()
    {
        EmbeddingModel.EnsureAllModelsLoaded();
        var model = EmbeddingModel.All.First(m => m.Id == "text-embedding-3-small");
        Assert.Equal("openai-text-embedding-3-small", model.ServiceKey);
    }
}
