namespace Core.AI;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter)]
public abstract class LlmAttributeBase : Attribute, IFacetMetadata
{
    public abstract string ServiceKey { get; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Parameter)]
public sealed class LlmAttribute<TModel> : LlmAttributeBase where TModel : LLMModel
{
    private readonly Lazy<string> _serviceKey;
    public override string ServiceKey => _serviceKey.Value;

    public LlmAttribute()
    {
        _serviceKey = new Lazy<string>(() =>
        {
            var model = LLMModel.All.FirstOrDefault(m => m.GetType() == typeof(TModel))
                ?? throw new InvalidOperationException(
                    $"LLM model {typeof(TModel).Name} not found in registry.");
            return model.ServiceKey;
        });
    }
}