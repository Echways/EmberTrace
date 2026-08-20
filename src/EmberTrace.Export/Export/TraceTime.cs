namespace EmberTrace.Export;

internal static class TraceTime
{
    public static double ToUs(long ticks, long freq)
    {
        return ticks * 1_000_000.0 / freq;
    }
}