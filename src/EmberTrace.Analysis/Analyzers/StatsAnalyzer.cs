using EmberTrace.Analysis.Model;
using EmberTrace.Analysis.Stats;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Analysis.Analyzers;

internal static class StatsAnalyzer
{
    public static TraceStats Analyze(TraceSession session, bool strict)
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
                agg = new Agg(session.TimestampFrequency);
                perId.Add(step.Id, agg);
            }

            agg.Add(step.DurationTicks);
        }

        var list = new List<TraceIdStats>(perId.Count);
        foreach (var (id, agg) in perId)
        {
            var histogram = agg.Histogram;
            var totalMs = conv.ToMs(agg.TotalTicks);

            list.Add(new TraceIdStats
            {
                Id = id,
                Count = histogram.Count,
                TotalMs = totalMs,
                AverageMs = totalMs / histogram.Count,
                MinMs = conv.ToMs(histogram.MinTicks),
                MaxMs = conv.ToMs(histogram.MaxTicks),
                Durations = histogram,
                P50Ms = histogram.PercentileMs(50),
                P90Ms = histogram.PercentileMs(90),
                P95Ms = histogram.PercentileMs(95),
                P99Ms = histogram.PercentileMs(99)
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

    private sealed class Agg(long timestampFrequency)
    {
        public DurationHistogram Histogram { get; } = new(timestampFrequency);
        public long TotalTicks { get; private set; }

        public void Add(long ticks)
        {
            Histogram.Add(ticks);
            TotalTicks += ticks;
        }
    }
}
