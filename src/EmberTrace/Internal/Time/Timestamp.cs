using System.Diagnostics;

namespace EmberTrace.Internal.Time;

internal static class Timestamp
{
    public static long Now() => Stopwatch.GetTimestamp();
    public static long Frequency => Stopwatch.Frequency;
}
