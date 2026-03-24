using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Core.AI;

namespace Aspire.Hosting;

public class LLMModelBuilder(IAWService iaw, LLMModel lastModel)
{
    internal IAWService IAW { get; } = iaw;
    internal LLMModel LastModel { get; } = lastModel;

    public IAWService AsFast()
    {
        IAW.TierMappings[LLMModel.All.OfType<Fast>().First().ServiceKey] = LastModel.ServiceKey;
        return IAW;
    }

    public IAWService AsBalanced()
    {
        IAW.TierMappings[LLMModel.All.OfType<Balanced>().First().ServiceKey] = LastModel.ServiceKey;
        return IAW;
    }

    public IAWService AsReasoning()
    {
        IAW.TierMappings[LLMModel.All.OfType<Reasoning>().First().ServiceKey] = LastModel.ServiceKey;
        return IAW;
    }

    public LLMModelBuilder WithLLM<TModel>() where TModel : LLMModel
        => IAW.WithLLM<TModel>();

    public IAWService WithOllama(Action<IResourceBuilder<OllamaResource>> configure)
        => IAW.WithOllama(configure);

    public IAWService WithVoice2Text()
        => IAW.WithVoice2Text();

    public IAWService WithVoice2Text<TModel>() where TModel : WhisperModel
        => IAW.WithVoice2Text<TModel>();

    public IAWService WithStorage(Action<IResourceBuilder<AzureStorageResource>> configure)
        => IAW.WithStorage(configure);

    public IAWService WithVectorDb(Action<IResourceBuilder<QdrantServerResource>> configure)
        => IAW.WithVectorDb(configure);

    public IAWService WithWorkspace(string path)
        => IAW.WithWorkspace(path);

    public IAWService WithCosmosStorage(IResourceBuilder<AzureCosmosDBResource> cosmos)
        => IAW.WithCosmosStorage(cosmos);

    public IAWClientService AsClient() => IAW.AsClient();

    public static implicit operator IAWService(LLMModelBuilder builder) => builder.IAW;
}