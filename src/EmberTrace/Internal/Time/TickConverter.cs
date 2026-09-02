using EmberTrace.Sessions;

namespace EmberTrace.Internal.Time;

internal readonly struct TickConverter
{
    private readonly long _frequency;

    public TickConverter(long frequency)
    {
        _frequency = frequency;
    }

    public static TickConverter FromSession(TraceSession session)
    {
        return new TickConverter(session.TimestampFrequency);
    }

    public double ToMs(long ticks)
    {
        return ticks * 1000.0 / _frequency;
    }

    public double ToUs(long ticks)
    {
        return ticks * 1_000_000.0 / _frequency;
    }

    public DateTime ToUtc(DateTimeOffset baseUtc, long ticksFromStart)
    {
        return baseUtc.UtcDateTime.AddTicks((long)(ticksFromStart * (double)TimeSpan.TicksPerSecond / _frequency));
    }
}
