using System.Diagnostics;

namespace EmberTrace.Internal.Time;

internal static class Timestamp
{
    public static long Frequency => Stopwatch.Frequency;

    public static long Now()
    {
        return Stopwatch.GetTimestamp();
    }
}