namespace EmberTrace.Tracing;

public readonly ref struct Scope
{
    private readonly int _id;
    private readonly bool _active;
    private readonly Profiler? _profiler;

    internal Scope(int id, Profiler? profiler, bool active)
    {
        _id = id;
        _profiler = profiler;
        _active = active;
    }

    public void Dispose()
    {
        if (_active)
            _profiler?.EndScope(_id);
    }
}
