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
            var histogram = agg.Histogram;
            var totalMs = conv.ToMs(agg.TotalTicks);
            var samples = agg.Samples;
            var sorted = samples is null ? null : samples.Order().ToArray();

            double PercentileMs(double percentile) => sorted is null
                ? histogram.PercentileMs(percentile)
                : conv.ToMs(SampleStatistics.PercentileTicks(sorted, percentile));

            list.Add(new TraceIdStats
            {
                Id = id,
                Count = histogram.Count,
                TotalMs = totalMs,
                AverageMs = totalMs / histogram.Count,
                MinMs = conv.ToMs(histogram.MinTicks),
                MaxMs = conv.ToMs(histogram.MaxTicks),
                Durations = histogram,
                P50Ms = PercentileMs(50),
                P90Ms = PercentileMs(90),
                P95Ms = PercentileMs(95),
                P99Ms = PercentileMs(99),
                Samples = samples is null ? ReadOnlyMemory<long>.Empty : samples.ToArray(),
                IsExact = samples is not null
            });
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

    private sealed class Agg(long timestampFrequency, int maxSamples)
    {
        private List<long>? _samples = maxSamples > 0 ? new List<long>() : null;

        public DurationHistogram Histogram { get; } = new(timestampFrequency);
        public long TotalTicks { get; private set; }
        public IReadOnlyList<long>? Samples => _samples;

        public void Add(long ticks)
        {
            Histogram.Add(ticks);
            TotalTicks += ticks;

            if (_samples is null)
                return;

            if (_samples.Count >= maxSamples)
                _samples = null;
            else
                _samples.Add(ticks);
        }
    }
}
