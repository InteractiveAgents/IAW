using System.Reflection;
using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Core.AI;

public sealed class UserProfileStateMapper : IAttributeToFactoryMapper<UserProfileStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        UserProfileStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(UserProfileDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type UserProfileDurableState.");

        return context =>
        {
            var services = context.ActivationServices;
            return new UserProfileDurableState(
                services.GetRequiredKeyedService<IDurableDictionary<string, string>>("user-preferences"),
                services.GetRequiredKeyedService<IDurableDictionary<string, string>>("user-projects"));
        };
    }
}
