using Microsoft.Extensions.DependencyInjection;

namespace Core.AI;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class LlmServiceKeyAttribute(string serviceKey) : FromKeyedServicesAttribute(serviceKey);
