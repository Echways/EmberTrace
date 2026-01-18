namespace EmberTrace.Internal.Time;

internal readonly struct TickConverter
{
    private readonly long _frequency;

    public TickConverter(long frequency)
    {
        _frequency = frequency;
    }

    public double ToMs(long ticks) => ticks * 1000.0 / _frequency;
}
