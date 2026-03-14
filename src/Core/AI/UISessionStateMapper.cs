using System.Reflection;
using Core.Contracts;
using Core.Contracts.UI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace Core.AI;

public sealed class UISessionStateMapper : IAttributeToFactoryMapper<UISessionStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        UISessionStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(UISessionDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type UISessionDurableState.");

        return context =>
        {
            var services = context.ActivationServices;
            return new UISessionDurableState(
                services.GetRequiredKeyedService<IDurableDictionary<string, PendingApproval>>("ui-pending-approvals"),
                services.GetRequiredKeyedService<IDurableDictionary<string, WizardState>>("ui-wizards"),
                services.GetRequiredKeyedService<IDurableDictionary<string, string>>("ui-pending-free-text"));
        };
    }
}
