using EmberTrace.Analysis.Model;
using EmberTrace.Analysis.Stats;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Analysis.Analyzers;

internal static class StatsAnalyzer
{
    public static TraceStats Analyze(TraceSession session, bool strict, int maxSamplesPerId)
    {
        var conv = TickConverter.FromSession(session);
        var perId = new Dictionary<int, Agg>(256);
        var reader = new ScopeReader(session, strict, session.Options.OnMismatchedEnd);

        foreach (var step in reader.Read())
        {
            if (step.Kind != ScopeStepKind.Close || step.IsSynthetic || step.DurationTicks < 0)
                continue;

            if (!perId.TryGetValue(step.Id, out var agg))
            {
                agg = new Agg(session.TimestampFrequency, maxSamplesPerId);
                perId.Add(step.Id, agg);
            }

            agg.Add(step.DurationTicks);
        }

        var list = new List<TraceIdStats>(perId.Count);
        foreach (var (id, agg) in perId)
        {
            var totalMs = conv.ToMs(agg.TotalTicks);
            var isExact = agg.Samples.IsExact;
            var samples = agg.Samples.Detach();

            list.Add(isExact ? Exact(id, agg, samples, totalMs, conv) : Approximate(id, agg, totalMs, conv));
        }

        list.Sort((x, y) => y.TotalMs.CompareTo(x.TotalMs));

        return new TraceStats
        {
            DurationMs = session.DurationMs,
            TotalEventCount = reader.TotalEventCount,
            ScopeEventCount = reader.ScopeEventCount,
            ThreadsSeen = reader.Tracks.Count,
            UnmatchedBeginCount = reader.UnmatchedBeginCount,
            UnmatchedEndCount = reader.UnmatchedEndCount,
            MismatchedEndCount = reader.MismatchedEndCount,
            ByTotalTimeDesc = list
        };
    }

    private static TraceIdStats Exact(int id, Agg agg, ReadOnlyMemory<long> samples, double totalMs, TickConverter conv)
    {
        using var sorted = SortedSamples.From(samples.Span);
        var percentiles = new Percentiles(
            PercentileMs(sorted, 50, conv), PercentileMs(sorted, 90, conv), PercentileMs(sorted, 95, conv),
            PercentileMs(sorted, 99, conv));
        return Build(id, agg, totalMs, conv, percentiles, samples,
            DistributionShape.FromSortedTicks(sorted.Values, conv));
    }

    private static double PercentileMs(SortedSamples sorted, double percentile, TickConverter conv)
    {
        return conv.ToMs(SampleStatistics.PercentileTicks(sorted.Values, percentile));
    }

    private static TraceIdStats Approximate(int id, Agg agg, double totalMs, TickConverter conv)
    {
        var histogram = agg.Histogram;
        var percentiles = new Percentiles(
            histogram.PercentileMs(50), histogram.PercentileMs(90), histogram.PercentileMs(95),
            histogram.PercentileMs(99));
        return Build(id, agg, totalMs, conv, percentiles, default, null);
    }

    private static TraceIdStats Build(
        int id,
        Agg agg,
        double totalMs,
        TickConverter conv,
        Percentiles percentiles,
        ReadOnlyMemory<long> samples,
        DistributionShape? shape)
    {
        var histogram = agg.Histogram;
        return new TraceIdStats
        {
            Id = id,
            Count = histogram.Count,
            TotalMs = totalMs,
            AverageMs = totalMs / histogram.Count,
            MinMs = conv.ToMs(histogram.MinTicks),
            MaxMs = conv.ToMs(histogram.MaxTicks),
            Durations = histogram,
            P50Ms = percentiles.P50,
            P90Ms = percentiles.P90,
            P95Ms = percentiles.P95,
            P99Ms = percentiles.P99,
            Samples = samples,
            IsExact = shape is not null,
            Shape = shape
        };
    }

    private readonly record struct Percentiles(double P50, double P90, double P95, double P99);

    private sealed class Agg(long timestampFrequency, int maxSamples)
    {
        public DurationHistogram Histogram { get; } = new(timestampFrequency);
        public SampleBuffer Samples { get; } = new(maxSamples);
        public long TotalTicks { get; private set; }

        public void Add(long ticks)
        {
            Histogram.Add(ticks);
            Samples.Add(ticks);
            TotalTicks += ticks;
        }
    }
}