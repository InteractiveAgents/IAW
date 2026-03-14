namespace Core.Contracts;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UISessionStateAttribute : Attribute, IFacetMetadata;
