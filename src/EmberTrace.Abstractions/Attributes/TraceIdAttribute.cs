using System;

namespace EmberTrace.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class TraceIdAttribute(int id, string name, string? category = null) : Attribute
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string? Category { get; } = category;
}