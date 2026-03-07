namespace IAW.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class DevVisibleAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}
