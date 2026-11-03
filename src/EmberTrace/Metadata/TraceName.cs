namespace EmberTrace.Metadata;

public readonly struct TraceName
{
    private readonly string? _name;

    public TraceName(string name, string? category = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        _name = name;
        Id = Tracer.RegisterId(name, category);
    }

    public int Id { get; }

    public string Name => _name ?? string.Empty;

    public override string ToString()
    {
        return Name;
    }
}
