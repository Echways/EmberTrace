using EmberTrace.Internal.Time;

namespace EmberTrace.Export;

[Obsolete("Use TickConverter.ToUs instead.")]
internal static class TraceTime
{
    public static double ToUs(long ticks, long freq)
    {
        return new TickConverter(freq).ToUs(ticks);
    }
}
