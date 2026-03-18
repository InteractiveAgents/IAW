namespace Core.Contracts;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class AgentStateAttribute : Attribute, IFacetMetadata;
