namespace Core.Contracts;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ProjectStateAttribute : Attribute, IFacetMetadata;
