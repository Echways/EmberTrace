namespace EmberTrace.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TraceAttribute : Attribute
{
    public TraceAttribute()
    {
    }

    public TraceAttribute(string name)
    {
        Name = name;
    }

    public string? Name { get; }

    public int Id { get; set; }

    public string? Category { get; set; }

    public Type? Interface { get; set; }
}
