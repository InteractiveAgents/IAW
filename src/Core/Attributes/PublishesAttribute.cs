namespace IAW.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PublishesAttribute(string eventName) : Attribute
{
    public string EventName { get; } = eventName;
}
