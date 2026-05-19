using System;

namespace EmberTrace.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TraceCategoryAttribute(string category) : Attribute
{
    public string Category { get; } = category;
}
