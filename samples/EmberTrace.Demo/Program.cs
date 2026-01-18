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
        var stats = session.Analyze();
        Console.WriteLine($"Events: {stats.TotalEvents}");
        Console.WriteLine($"Threads: {stats.ThreadsSeen}");
        Console.WriteLine($"MismatchedEnd: {stats.MismatchedEndCount}");
        Console.WriteLine($"Duration: {stats.DurationMs:F3} ms");

        var top = stats.ByTotalTimeDesc;
        for (int i = 0; i < Math.Min(5, top.Count); i++)
        {
            var s = top[i];
            Console.WriteLine($"{s.Id}  count={s.Count}  total={s.TotalMs:F3}ms  avg={s.AverageMs:F3}ms  min={s.MinMs:F3}ms  max={s.MaxMs:F3}ms");
        }

    }
}
