namespace EmberTrace.Tracing;

public readonly ref struct Scope
{
    private readonly int _id;
    private readonly bool _active;

    internal Scope(int id, bool active)
    {
        _id = id;
        _active = active;
    }

    public void Dispose()
    {
        if (_active)
            Profiler.End(_id);
    }
}
