using Core.AI;
using Core.AI.Models;
using Xunit;

namespace IAW.Core.Tests.Models;

public class LLMModelTests
{
    [Fact]
    public void Opus46_has_correct_id()
    {
        var model = LLMModel.All.First(m => m is Opus46);
        Assert.Equal("claude-opus-4-6", model.Id);
    }

    [Fact]
    public void Opus46_is_anthropic_provider()
    {
        var model = LLMModel.All.First(m => m is Opus46);
        Assert.Equal("anthropic", model.Provider);
    }

    [Fact]
    public void Gpt52_has_correct_id()
    {
        var model = LLMModel.All.First(m => m is Gpt52);
        Assert.Equal("gpt-5.2", model.Id);
    }

    [Fact]
    public void Gpt52_is_openai_provider()
    {
        var model = LLMModel.All.First(m => m is Gpt52);
        Assert.Equal("openai", model.Provider);
    }

    [Fact]
    public void Gpt53_has_correct_id()
    {
        var model = LLMModel.All.First(m => m is Gpt53);
        Assert.Equal("gpt-5.3", model.Id);
    }

    [Fact]
    public void Gpt53_is_openai_provider()
    {
        var model = LLMModel.All.First(m => m is Gpt53);
        Assert.Equal("openai", model.Provider);
    }

    [Fact]
    public void Gemini31_has_correct_id()
    {
        var model = LLMModel.All.First(m => m is Gemini31);
        Assert.Equal("gemini-3.1", model.Id);
    }

    [Fact]
    public void Gemini31_is_openai_provider()
    {
        var model = LLMModel.All.First(m => m is Gemini31);
        Assert.Equal("openai", model.Provider);
    }

    [Fact]
    public void GrokLatest_has_correct_id()
    {
        var model = LLMModel.All.First(m => m is GrokLatest);
        Assert.Equal("grok-latest", model.Id);
    }

    [Fact]
    public void GrokLatest_is_openai_provider()
    {
        var model = LLMModel.All.First(m => m is GrokLatest);
        Assert.Equal("openai", model.Provider);
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
        Assert.Contains("qwen2.5:7b", allIds);
        Assert.Contains("qwen2.5:14b", allIds);
    }

    [Fact]
    public void All_new_models_are_fully_capable()
    {
        Assert.Equal(ModelCapabilities.FullyCapable, LLMModel.All.First(m => m is Opus46).Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, LLMModel.All.First(m => m is Gpt52).Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, LLMModel.All.First(m => m is Gpt53).Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, LLMModel.All.First(m => m is Gemini31).Capabilities);
        Assert.Equal(ModelCapabilities.FullyCapable, LLMModel.All.First(m => m is GrokLatest).Capabilities);
    }

    [Fact]
    public void Qwen25_7B_has_correct_ollama_tag()
    {
        var model = LLMModel.All.First(m => m is Qwen25_7B);
        Assert.Equal("qwen2.5:7b", model.Id);
        Assert.Equal("ollama", model.Provider);
        Assert.True(model.IsLocal);
        Assert.Equal("ollama-qwen25-7b", model.ServiceKey);
    }

    [Fact]
    public void Qwen25_14B_has_correct_ollama_tag()
    {
        var model = LLMModel.All.First(m => m is Qwen25_14B);
        Assert.Equal("qwen2.5:14b", model.Id);
        Assert.Equal("ollama", model.Provider);
        Assert.True(model.IsLocal);
        Assert.Equal("ollama-qwen25-14b", model.ServiceKey);
    }

    [Fact]
    public void Qwen25_sized_variants_are_chat_only()
    {
        Assert.Equal(ModelCapabilities.ChatOnly, LLMModel.All.First(m => m is Qwen25_7B).Capabilities);
        Assert.Equal(ModelCapabilities.ChatOnly, LLMModel.All.First(m => m is Qwen25_14B).Capabilities);
    }

    [Fact]
    public void Qwen25_sized_variants_have_unique_service_keys()
    {
        var keys = LLMModel.All
            .Where(m => m is Qwen25 or Qwen25_7B or Qwen25_14B)
            .Select(m => m.ServiceKey)
            .ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void RegisterCustomModel_AppearsInRegistry()
    {
        var id = $"test-register-{Guid.NewGuid():N}";
        var model = LLMModel.Register(id, "openai", "My Fine-Tuned GPT");
        Assert.Contains(LLMModel.All, m => m.Id == id);
        Assert.Equal($"openai-{id}", model.ServiceKey);
    }

    [Fact]
    public void RegisterCustomModel_WithCapabilities()
    {
        var id = $"test-caps-{Guid.NewGuid():N}";
        var model = LLMModel.Register(id, "ollama", "Custom Local", ModelCapabilities.ChatOnly);
        Assert.Equal("ollama", model.Provider);
        Assert.True(model.IsLocal);
        Assert.Equal(ModelCapabilities.ChatOnly, model.Capabilities);
    }

    [Fact]
    public void RegisterCustomModel_DefaultsToFullyCapable()
    {
        var id = $"test-default-{Guid.NewGuid():N}";
        var model = LLMModel.Register(id, "custom-provider", "Default Caps");
        Assert.Equal(ModelCapabilities.FullyCapable, model.Capabilities);
    }

    [Fact]
    public void RegisterDuplicateModel_Throws()
    {
        var id = $"test-dup-{Guid.NewGuid():N}";
        LLMModel.Register(id, "openai", "First");
        Assert.Throws<InvalidOperationException>(() => LLMModel.Register(id, "openai", "Second"));
    }
}