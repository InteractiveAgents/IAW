using Core.AI;
using Core.AI.Models;
using Xunit;

namespace Core.Tests.Models;

public class LLMModelTests
{
    [Fact]
    public void Opus46_has_correct_id()
    {
        Assert.Equal("claude-opus-4-6", Opus46.Instance.Id);
    }

    [Fact]
    public void Opus46_is_anthropic_provider()
    {
        Assert.Equal(ProviderType.Anthropic, Opus46.Instance.Provider);
    }

    [Fact]
    public void Gpt52_has_correct_id()
    {
        Assert.Equal("gpt-5.2", Gpt52.Instance.Id);
    }

    [Fact]
    public void Gpt52_is_openai_provider()
    {
        Assert.Equal(ProviderType.OpenAI, Gpt52.Instance.Provider);
    }

    [Fact]
    public void Gpt53_has_correct_id()
    {
        Assert.Equal("gpt-5.3", Gpt53.Instance.Id);
    }

    [Fact]
    public void Gpt53_is_openai_provider()
    {
        Assert.Equal(ProviderType.OpenAI, Gpt53.Instance.Provider);
    }

    [Fact]
    public void Gemini31_has_correct_id()
    {
        Assert.Equal("gemini-3.1", Gemini31.Instance.Id);
    }

    [Fact]
    public void Gemini31_is_openai_provider()
    {
        Assert.Equal(ProviderType.OpenAI, Gemini31.Instance.Provider);
    }

    [Fact]
    public void GrokLatest_has_correct_id()
    {
        Assert.Equal("grok-latest", GrokLatest.Instance.Id);
    }

    [Fact]
    public void GrokLatest_is_openai_provider()
    {
        Assert.Equal(ProviderType.OpenAI, GrokLatest.Instance.Provider);
    }

    [Fact]
    public void EnsureAllModelsLoaded_includes_all_new_models()
    {
        LLMModel.EnsureAllModelsLoaded();
        var allIds = LLMModel.All.Select(m => m.Id).ToHashSet();

        Assert.Contains("claude-opus-4-6", allIds);
        Assert.Contains("gpt-5.2", allIds);
        Assert.Contains("gpt-5.3", allIds);
        Assert.Contains("gemini-3.1", allIds);
        Assert.Contains("grok-latest", allIds);
    }

    [Fact]
    public void All_new_models_are_fully_capable()
    {
        Assert.Equal(ModelCapabilities.FullyCapable, Opus46.Instance.Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, Gpt52.Instance.Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, Gpt53.Instance.Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, Gemini31.Instance.Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, GrokLatest.Instance.Capabilities);
    }
}
