namespace Core.Contracts;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class UserProfileStateAttribute : Attribute, IFacetMetadata;