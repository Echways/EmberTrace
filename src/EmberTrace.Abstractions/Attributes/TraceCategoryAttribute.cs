namespace EmberTrace.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method)]
public sealed class TraceCategoryAttribute(string category) : Attribute
{
    public string Category { get; } = category;
}
