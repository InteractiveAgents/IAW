using Microsoft.Extensions.DependencyInjection;

namespace Core.Contracts;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MemoryAttribute(string name) : FromKeyedServicesAttribute(name);