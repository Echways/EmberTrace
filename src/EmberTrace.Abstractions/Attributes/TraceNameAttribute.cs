using System;

namespace EmberTrace.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public sealed class TraceNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
