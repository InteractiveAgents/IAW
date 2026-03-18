namespace Core.AI;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter)]
public abstract class EmbeddingAttributeBase : Attribute, IFacetMetadata
{
    public abstract string ServiceKey { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter)]
public sealed class EmbeddingAttribute<TModel> : EmbeddingAttributeBase where TModel : EmbeddingModel
{
    private readonly Lazy<string> _serviceKey;
    public override string ServiceKey => _serviceKey.Value;

    public EmbeddingAttribute()
    {
        _serviceKey = new Lazy<string>(() =>
        {
            var model = EmbeddingModel.All.FirstOrDefault(m => m.GetType() == typeof(TModel))
                ?? throw new InvalidOperationException(
                    $"Embedding model {typeof(TModel).Name} not found in registry.");
            return model.ServiceKey;
        });
    }
}
