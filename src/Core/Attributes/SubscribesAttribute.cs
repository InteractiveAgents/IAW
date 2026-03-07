namespace IAW.Core.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class SubscribesAttribute(string eventName) : Attribute
{
    public string EventName { get; } = eventName;
}
