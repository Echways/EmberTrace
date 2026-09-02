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
            if (step.Kind != ScopeStepKind.Close || step.IsSynthetic)
                continue;

            var dtTicks = step.DurationTicks;
            if (dtTicks < 0)
                continue;

            if (!perId.TryGetValue(step.Id, out var agg))
            {
                agg = new Agg();
                perId.Add(step.Id, agg);
            }

            agg.Add(dtTicks, conv);
        }

        var list = new List<TraceIdStats>(perId.Count);
        foreach (var kv in perId)
        {
            var id = kv.Key;
            var a = kv.Value;
            var min = double.IsPositiveInfinity(a.MinMs) ? 0 : a.MinMs;

            list.Add(new TraceIdStats
            {
                Id = id,
                Count = a.Count,
                TotalMs = a.TotalMs,
                AverageMs = a.Count == 0 ? 0 : a.TotalMs / a.Count,
                MinMs = min,
                MaxMs = a.MaxMs,
                Durations = a.Histogram,
                P50Ms = conv.ToMs(a.Histogram.PercentileTicks(50)),
                P90Ms = conv.ToMs(a.Histogram.PercentileTicks(90)),
                P95Ms = conv.ToMs(a.Histogram.PercentileTicks(95)),
                P99Ms = conv.ToMs(a.Histogram.PercentileTicks(99))
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

    private sealed class Agg
    {
        public readonly DurationHistogram Histogram = new();
        public long Count;
        public double MaxMs;
        public double MinMs = double.PositiveInfinity;
        public double TotalMs;

        public void Add(long ticks, TickConverter conv)
        {
            var ms = conv.ToMs(ticks);

            Count++;
            TotalMs += ms;
            if (ms < MinMs) MinMs = ms;
            if (ms > MaxMs) MaxMs = ms;

            Histogram.Add(ticks);
        }
    }
}
