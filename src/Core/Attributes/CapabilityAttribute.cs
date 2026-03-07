namespace IAW.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class CapabilityAttribute(string capability) : Attribute
{
    public string Capability { get; } = capability;
}
