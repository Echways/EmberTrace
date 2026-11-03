using System.Buffers;

namespace EmberTrace.Analysis.Stats;

internal readonly struct SortedSamples : IDisposable
{
    private readonly long[] _rented;
    private readonly int _count;

    private SortedSamples(long[] rented, int count)
    {
        _rented = rented;
        _count = count;
    }

    public ReadOnlySpan<long> Values => _rented.AsSpan(0, _count);

    public static SortedSamples From(ReadOnlySpan<long> samples)
    {
        var rented = ArrayPool<long>.Shared.Rent(samples.Length);
        var sorted = rented.AsSpan(0, samples.Length);
        samples.CopyTo(sorted);
        sorted.Sort();
        return new SortedSamples(rented, samples.Length);
    }

    public void Dispose()
    {
        ArrayPool<long>.Shared.Return(_rented);
    }
}