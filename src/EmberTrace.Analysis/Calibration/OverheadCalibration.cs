using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace EmberTrace.Analysis.Calibration;

public static class OverheadCalibration
{
    private const int Repetitions = 5;
    private const int TimerSamples = 100;
    private static readonly int[] DefaultBatchSizes = [1, 10, 100, 1000];

    private static long _sink;

    public static OverheadReport Measure(IReadOnlyList<int>? batchSizes = null, int iterations = 200_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(iterations);

        var sizes = batchSizes ?? DefaultBatchSizes;
        foreach (var size in sizes)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size, nameof(batchSizes));

        if (Tracer.IsRunning)
            throw new InvalidOperationException("Overhead calibration needs the tracer to be stopped.");

        var id = Tracer.Id("EmberTrace.Overhead.Calibration");
        var scopeNs = NsPerItem(1, iterations, id);
        var byBatch = new Dictionary<int, double>(sizes.Count);
        foreach (var size in sizes)
            byBatch[size] = size == 1 ? scopeNs : NsPerItem(size, iterations, id);

        return new OverheadReport(scopeNs, TimerQuantumNs(), byBatch);
    }

    private static double NsPerItem(int batch, int iterations, int id)
    {
        var outer = Math.Max(1, iterations / batch);
        var items = (double)outer * batch;

        TimeScoped(outer, batch, id);
        RunBare(outer, batch);

        var costs = new double[Repetitions];
        for (var i = 0; i < Repetitions; i++)
        {
            var scoped = TimeScoped(outer, batch, id);
            var bare = Time(() => RunBare(outer, batch));
            costs[i] = Math.Max(0, (scoped - bare) / items);
        }

        Array.Sort(costs);
        return costs[Repetitions / 2];
    }

    private static double TimeScoped(int outer, int batch, int id)
    {
        Tracer.Start();
        try
        {
            return Time(() => RunScoped(outer, batch, id));
        }
        finally
        {
            Tracer.Stop();
        }
    }

    private static double Time(Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(start).TotalNanoseconds;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunScoped(int outer, int batch, int id)
    {
        var sink = _sink;
        for (var o = 0; o < outer; o++)
            using (Tracer.Scope(id))
            {
                for (var b = 0; b < batch; b++)
                    sink = Work(sink);
            }

        _sink = sink;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunBare(int outer, int batch)
    {
        var sink = _sink;
        for (var o = 0; o < outer; o++)
            for (var b = 0; b < batch; b++)
                sink = Work(sink);

        _sink = sink;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long Work(long value)
    {
        return value * 2654435761L + 1;
    }

    private static double TimerQuantumNs()
    {
        var smallest = long.MaxValue;
        var previous = Stopwatch.GetTimestamp();
        for (var seen = 0; seen < TimerSamples;)
        {
            var now = Stopwatch.GetTimestamp();
            if (now == previous)
                continue;

            smallest = Math.Min(smallest, now - previous);
            previous = now;
            seen++;
        }

        return smallest * 1_000_000_000.0 / Stopwatch.Frequency;
    }
}