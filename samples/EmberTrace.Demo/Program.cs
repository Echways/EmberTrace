using System;
using System.Threading;
using EmberTrace.Public;

internal static class Program
{
    public static void Main()
    {
        var opts = new SessionOptions { ChunkCapacity = 200_000, OverflowPolicy = OverflowPolicy.Drop };

        Profiler.Start(opts);

        for (int i = 0; i < 200; i++)
        {
            using var _ = Profiler.Scope(1001);
            Thread.SpinWait(20_000);
        }

        var session = Profiler.Stop();
        Console.WriteLine($"Events: {session.EventCount}");
        Console.WriteLine($"Duration: {session.DurationMs:F3} ms");
    }
}
