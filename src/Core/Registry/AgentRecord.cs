using Microsoft.Extensions.VectorData;

namespace Core.Registry;

[GenerateSerializer]
public sealed class AgentRecord
{
    [VectorStoreKey]
    [Id(0)]
    public Guid Id { get; set; }

    [VectorStoreData(IsIndexed = true)]
    [Id(1)]
    public string Namespace { get; set; } = "";

    [VectorStoreData(IsIndexed = true)]
    [Id(2)]
    public string AgentType { get; set; } = "";

    [VectorStoreData]
    [Id(3)]
    public string DisplayName { get; set; } = "";

    [VectorStoreData(IsFullTextIndexed = true)]
    [Id(4)]
    public string Description { get; set; } = "";

    [VectorStoreData]
    [Id(5)]
    public string[] Capabilities { get; set; } = [];

    [VectorStoreData(IsIndexed = true)]
    [Id(6)]
    public string InterfaceName { get; set; } = "";

    [VectorStoreVector(Dimensions: 1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    [Id(7)]
    public ReadOnlyMemory<float> DescriptionEmbedding { get; set; }
}
