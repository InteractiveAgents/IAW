using Microsoft.Extensions.DependencyInjection;

namespace IAW.Core;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class MemoryAttribute(string name) : FromKeyedServicesAttribute(name);
