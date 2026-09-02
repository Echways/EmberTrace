namespace EmberTrace.Internal.Buffering;

internal sealed class ThreadNameRegistry
{
    private readonly Dictionary<int, string> _names = new();
    private readonly object _sync = new();

    public void Set(int threadId, string name)
    {
        lock (_sync)
        {
            if (!_names.ContainsKey(threadId))
                _names.Add(threadId, name);
        }
    }

    public IReadOnlyDictionary<int, string> Snapshot()
    {
        lock (_sync)
        {
            return new Dictionary<int, string>(_names);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _names.Clear();
        }
    }
}
