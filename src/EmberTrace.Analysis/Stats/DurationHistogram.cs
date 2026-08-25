using System.Numerics;

namespace EmberTrace.Analysis.Stats;

public sealed class DurationHistogram
{
    private const int ExplicitBits = 5;
    private const int SubBucketCount = 1 << (ExplicitBits + 1);
    private const int BucketsPerMagnitude = 1 << ExplicitBits;
    private const int MinMagnitude = ExplicitBits + 1;
    private const int MaxMagnitude = 62;

    internal const int BucketCount = SubBucketCount + (MaxMagnitude - MinMagnitude + 1) * BucketsPerMagnitude;

    private int[] _counts = Array.Empty<int>();

    public long Count { get; private set; }
    public long MinTicks { get; private set; }
    public long MaxTicks { get; private set; }

    public void Add(long ticks)
    {
        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Duration ticks must not be negative.");

        var index = BucketIndexOf(ticks);
        if (index >= _counts.Length)
            Grow(index);

        _counts[index]++;

        if (Count == 0 || ticks < MinTicks) MinTicks = ticks;
        if (Count == 0 || ticks > MaxTicks) MaxTicks = ticks;

        Count++;
    }

    public long PercentileTicks(double percentile)
    {
        if (percentile is < 0 or > 100 || double.IsNaN(percentile))
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be in [0, 100].");

        if (Count == 0)
            return 0;

        var rank = (long)Math.Ceiling(percentile / 100.0 * Count);
        if (rank < 1)
            rank = 1;

        long seen = 0;
        for (var i = 0; i < _counts.Length; i++)
        {
            seen += _counts[i];
            if (seen >= rank)
                return Math.Min(UpperBoundOf(i), MaxTicks);
        }

        return MaxTicks;
    }

    internal static int BucketIndexOf(long value)
    {
        if (value < SubBucketCount)
            return (int)value;

        var magnitude = 63 - BitOperations.LeadingZeroCount((ulong)value);
        var shift = magnitude - ExplicitBits;
        var sub = (int)((value >> shift) & (BucketsPerMagnitude - 1));

        return SubBucketCount + (magnitude - MinMagnitude) * BucketsPerMagnitude + sub;
    }

    internal static long UpperBoundOf(int index)
    {
        if (index < SubBucketCount)
            return index;

        var offset = index - SubBucketCount;
        var magnitude = MinMagnitude + offset / BucketsPerMagnitude;
        var sub = offset % BucketsPerMagnitude;
        var shift = magnitude - ExplicitBits;

        var lower = (long)(BucketsPerMagnitude + sub) << shift;
        return lower + (1L << shift) - 1;
    }

    private void Grow(int index)
    {
        var size = Math.Max(64, _counts.Length);
        while (size <= index)
            size *= 2;

        if (size > BucketCount)
            size = BucketCount;

        Array.Resize(ref _counts, size);
    }
}
