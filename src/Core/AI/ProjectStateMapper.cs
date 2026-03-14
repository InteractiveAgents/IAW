using System.Reflection;
using Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using ChatMessage = Core.Contracts.ChatMessage;

namespace Core.AI;

public sealed class ProjectStateMapper : IAttributeToFactoryMapper<ProjectStateAttribute>
{
    public Factory<IGrainContext, object> GetFactory(
        ParameterInfo parameter,
        ProjectStateAttribute metadata)
    {
        if (parameter.ParameterType != typeof(ProjectDurableState))
            throw new InvalidOperationException(
                $"Parameter '{parameter.Name}' must be of type ProjectDurableState.");

        return context =>
        {
            var services = context.ActivationServices;
            return new ProjectDurableState(
                services.GetRequiredKeyedService<IDurableDictionary<string, StateEntry>>("agent-state"),
                services.GetRequiredKeyedService<IDurableList<AgentEvent>>("agent-events"),
                services.GetRequiredKeyedService<IDurableList<ChatMessage>>("history"),
                services.GetRequiredKeyedService<IDurableDictionary<string, TrackingItem>>("tracking"),
                services.GetRequiredKeyedService<IDurableList<ProjectTask>>("project-tasks"),
                services.GetRequiredKeyedService<IDurableDictionary<string, ScheduledJob>>("project-schedules"),
                services.GetRequiredKeyedService<IDurableDictionary<string, FileReference>>("project-files"),
                services.GetRequiredKeyedService<IDurableDictionary<string, string>>("project-meta"));
        };
    }
}
