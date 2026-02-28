using Microsoft.Extensions.DependencyInjection;

namespace Core;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MemoryAttribute(string name) : FromKeyedServicesAttribute(name);
