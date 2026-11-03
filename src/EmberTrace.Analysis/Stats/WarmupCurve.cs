namespace EmberTrace.Analysis.Stats;

public sealed record WarmupCurve(IReadOnlyList<double> Windows)
{
    internal static WarmupCurve FromSamples(ReadOnlySpan<long> samples, int windows, Func<long, double> toMs)
    {
        if (samples.IsEmpty)
            return new WarmupCurve([]);

        var count = Math.Min(windows, samples.Length);
        var medians = new double[count];
        var scratch = new long[(samples.Length + count - 1) / count];

        for (var i = 0; i < count; i++)
        {
            var from = (int)((long)i * samples.Length / count);
            var to = (int)((long)(i + 1) * samples.Length / count);
            var window = scratch.AsSpan(0, to - from);
            samples[from..to].CopyTo(window);
            window.Sort();
            medians[i] = toMs(SampleStatistics.PercentileTicks(window, 50));
        }

        return new WarmupCurve(medians);
    }
}
