namespace EmberTrace.Analysis.Stats;

internal sealed class SampleBuffer(int limit)
{
    internal const int DefaultLimit = 100_000;

    private List<long>? _samples = limit > 0 ? new List<long>() : null;

    public bool IsExact => _samples is not null;

    public void Add(long ticks)
    {
        if (_samples is null)
            return;

        if (_samples.Count >= limit)
            _samples = null;
        else
            _samples.Add(ticks);
    }

    public long[] ToArray()
    {
        return _samples?.ToArray() ?? [];
    }
}
