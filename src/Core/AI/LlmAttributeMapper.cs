using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace Core.AI;

public sealed class LlmAttributeMapper<TModel>
    : IAttributeToFactoryMapper<LlmAttribute<TModel>>
    where TModel : LLMModel
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        LlmAttribute<TModel> metadata)
    {
        if (parameter.ParameterType != typeof(IChatClient))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type IChatClient.");

        return context =>
        {
            var chatClient = context.ActivationServices
                .GetKeyedService<IChatClient>(metadata.ServiceKey)
                ?? throw new InvalidOperationException(
                    $"LLM model '{typeof(TModel).Name}' not configured. " +
                    $"Service key: '{metadata.ServiceKey}'.");
            return chatClient;
        };
    }
}
