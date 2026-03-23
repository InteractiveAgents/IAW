using Microsoft.Extensions.AI;

namespace IAW.Testing;

public sealed class MockEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public EmbeddingGeneratorMetadata Metadata => new("mock");

    public object? GetService(Type serviceType, object? key = null) => null;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var embeddings = values.Select(_ => new Embedding<float>(new float[384])).ToList();
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public void Dispose() { }
}