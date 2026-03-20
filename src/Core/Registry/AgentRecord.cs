using Microsoft.Extensions.VectorData;

namespace Core.Registry;

public sealed class AgentRecord
{
    [VectorStoreKey]
    public Guid Id { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public string Namespace { get; set; } = "";

    [VectorStoreData(IsIndexed = true)]
    public string AgentType { get; set; } = "";

    [VectorStoreData]
    public string DisplayName { get; set; } = "";

    [VectorStoreData(IsFullTextIndexed = true)]
    public string Description { get; set; } = "";

    [VectorStoreData]
    public string[] Capabilities { get; set; } = [];

    [VectorStoreData(IsIndexed = true)]
    public string InterfaceName { get; set; } = "";

    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> DescriptionEmbedding { get; set; }
}
