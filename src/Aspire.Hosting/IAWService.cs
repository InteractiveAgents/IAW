using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Orleans;
using Core.AI;

namespace Aspire.Hosting;

public class IAWService(OrleansService orleans, IDistributedApplicationBuilder appBuilder)
{
    internal OrleansService Orleans { get; } = orleans;
    internal IDistributedApplicationBuilder AppBuilder { get; } = appBuilder;
    internal List<LLMModel> DeclaredModels { get; } = [];
    internal HashSet<string> DeclaredProviders { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal IResourceBuilder<OllamaResource>? OllamaResource { get; set; }
    internal List<IResourceBuilder<OllamaModelResource>> OllamaModelResources { get; } = [];
    internal WhisperModel? WhisperModel { get; set; }
    internal EmbeddingModel? DeclaredEmbeddingModel { get; set; }

    internal IResourceBuilder<AzureStorageResource> Storage { get; set; } = null!;
    internal IResourceBuilder<AzureBlobStorageResource> Blobs { get; set; } = null!;
    internal IResourceBuilder<QdrantServerResource> VectorDb { get; set; } = null!;

    internal Action<IResourceBuilder<AzureStorageResource>>? StorageCallback { get; set; }
    internal Action<IResourceBuilder<QdrantServerResource>>? VectorDbCallback { get; set; }

    internal IResourceBuilder<ParameterResource>? AnthropicKeyParam { get; set; }
    internal IResourceBuilder<ParameterResource>? OpenAiKeyParam { get; set; }
    internal IResourceBuilder<ParameterResource> GitHubTokenParam { get; set; } = null!;

    internal Dictionary<string, string> TierMappings { get; } = [];

    internal bool InfrastructureApplied { get; set; }
    internal string? WorkspacePath { get; set; }

    public IAWClientService AsClient() => new(this);
}

public class IAWClientService(IAWService service)
{
    internal IAWService IAW { get; } = service;
    internal OrleansServiceClient OrleansClient { get; } = service.Orleans.AsClient();
}