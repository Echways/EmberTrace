namespace EmberTrace.Analysis.Stats;

internal sealed class SampleBuffer(int limit)
{
    internal const int DefaultLimit = 100_000;

    private const int InitialCapacity = 16;

    private long[] _items = [];
    private int _count;

    public bool IsExact { get; private set; } = limit > 0;

    public ReadOnlySpan<long> Span => _items.AsSpan(0, _count);

    public void Add(long ticks)
    {
        if (!IsExact)
            return;

        if (_count >= limit)
        {
            IsExact = false;
            _items = [];
            _count = 0;
            return;
        }

        if (_count == _items.Length)
            Array.Resize(ref _items, (int)Math.Min(limit, Math.Max(InitialCapacity, _items.Length * 2L)));

        _items[_count++] = ticks;
    }

    public ReadOnlyMemory<long> Detach()
    {
        if (_count != _items.Length)
            Array.Resize(ref _items, _count);

        var detached = _items;
        _items = [];
        _count = 0;
        return detached;
    }
}