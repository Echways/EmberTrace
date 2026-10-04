namespace EmberTrace.Export;

internal sealed class EnclosingSlices
{
    private readonly Dictionary<int, int> _open = new();
    private readonly Dictionary<int, long> _top = new();

    public void Begin(int trackId)
    {
        _open[trackId] = _open.GetValueOrDefault(trackId) + 1;
        _top[trackId] = long.MaxValue;
    }

    public void End(int trackId)
    {
        var open = Math.Max(0, _open.GetValueOrDefault(trackId) - 1);
        _open[trackId] = open;

        if (open > 0)
            _top[trackId] = long.MaxValue;
        else
            _top.Remove(trackId);
    }

    public void Complete(int trackId, long endTimestamp)
    {
        _top[trackId] = endTimestamp;
    }

    public void Point(int trackId)
    {
        _top.Remove(trackId);
    }

    public bool Encloses(int trackId, long timestamp)
    {
        return _top.TryGetValue(trackId, out var end) && end >= timestamp;
    }
}
