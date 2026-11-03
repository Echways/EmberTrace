namespace EmberTrace.Analysis.Stats;

internal static class SampleStatistics
{
    public static long PercentileTicks(ReadOnlySpan<long> sorted, double percentile)
    {
        if (sorted.IsEmpty)
            return 0;

        var rank = (long)Math.Ceiling(percentile / 100.0 * sorted.Length);
        rank = Math.Clamp(rank, 1, sorted.Length);
        return sorted[(int)(rank - 1)];
    }
}