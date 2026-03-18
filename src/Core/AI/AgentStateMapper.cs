using System.Reflection;
using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace Core.AI;

public sealed class AgentStateMapper : IAttributeToFactoryMapper<AgentStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        AgentStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(AgentDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type AgentDurableState.");

        return context =>
        {
            var services = context.ActivationServices;
            return new AgentDurableState(
                services.GetRequiredKeyedService<IDurableDictionary<string, StateEntry>>("agent-state"),
                services.GetRequiredKeyedService<IDurableList<AgentEvent>>("agent-events"),
                services.GetRequiredKeyedService<IDurableList<ChatMessage>>("history"),
                services.GetRequiredKeyedService<IDurableDictionary<string, TrackingItem>>("tracking"));
        };
    }
}
