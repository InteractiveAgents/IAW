using Core.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Core.AI;

public static class LlmAttributeMapperRegistration
{
    public static void RegisterAttributeMapper(IServiceCollection services, LLMModel model)
    {
        var modelType = model.GetType();
        var mapperType = typeof(LlmAttributeMapper<>).MakeGenericType(modelType);
        var attributeType = typeof(LlmAttribute<>).MakeGenericType(modelType);
        var interfaceType = typeof(IAttributeToFactoryMapper<>).MakeGenericType(attributeType);
        services.AddSingleton(interfaceType, mapperType);
    }

    public static void RegisterAllAttributeMappers(IServiceCollection services)
    {
        LLMModel.EnsureAllModelsLoaded();

        foreach (var model in LLMModel.All)
            RegisterAttributeMapper(services, model);

        services.AddSingleton<IAttributeToFactoryMapper<AgentStateAttribute>, AgentStateMapper>();
        services.AddSingleton<IAttributeToFactoryMapper<UserProfileStateAttribute>, UserProfileStateMapper>();
        services.AddSingleton<IAttributeToFactoryMapper<UISessionStateAttribute>, UISessionStateMapper>();
    }

    public static void RegisterAllAttributeMappers(IServiceCollection services, IChatClient mockClient)
    {
        RegisterAllAttributeMappers(services);

        foreach (var model in LLMModel.All)
            services.AddKeyedSingleton<IChatClient>(model.ServiceKey, mockClient);
    }
}
