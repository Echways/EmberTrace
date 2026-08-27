namespace EmberTrace.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
public sealed class TraceNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
