using System.Globalization;
using EmberTrace.Analysis.Stats;

namespace EmberTrace.Testing;

public readonly struct ScopeAssertion
{
    private readonly string _label;
    private readonly TraceIdStats? _stats;

    internal ScopeAssertion(string label, TraceIdStats? stats)
    {
        _label = label;
        _stats = stats;
    }

    public ScopeAssertion NotRecorded()
    {
        if (_stats is not null)
            throw Failed($"expected {_label} to be absent, but it was recorded {_stats.Count} time(s)");

        return this;
    }

    public ScopeAssertion CountExactly(long expected)
    {
        var actual = Require().Count;
        if (actual != expected)
            throw Failed($"expected {_label} to be recorded exactly {expected} time(s), but it was recorded {actual}");

        return this;
    }

    public ScopeAssertion CountAtMost(long expected)
    {
        var actual = Require().Count;
        if (actual > expected)
            throw Failed($"expected {_label} to be recorded at most {expected} time(s), but it was recorded {actual}");

        return this;
    }

    public ScopeAssertion CountAtLeast(long expected)
    {
        var actual = Require().Count;
        if (actual < expected)
            throw Failed($"expected {_label} to be recorded at least {expected} time(s), but it was recorded {actual}");

        return this;
    }

    public ScopeAssertion TotalMsUnder(double ms)
    {
        return Compare("total", Require().TotalMs, ms);
    }

    public ScopeAssertion AverageMsUnder(double ms)
    {
        return Compare("average", Require().AverageMs, ms);
    }

    public ScopeAssertion MaxMsUnder(double ms)
    {
        return Compare("max", Require().MaxMs, ms);
    }

    public ScopeAssertion P50MsUnder(double ms)
    {
        return Compare("p50", Require().P50Ms, ms);
    }

    public ScopeAssertion P95MsUnder(double ms)
    {
        return Compare("p95", Require().P95Ms, ms);
    }

    public ScopeAssertion P99MsUnder(double ms)
    {
        return Compare("p99", Require().P99Ms, ms);
    }

    public ScopeAssertion PercentileMsUnder(double percentile, double ms)
    {
        if (percentile is < 0 or > 100 || double.IsNaN(percentile))
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Percentile must be in [0, 100].");

        var stats = Require();
        var histogram = stats.Durations
                        ?? throw Failed($"{_label} carries no duration histogram; produce stats with Analyze()");

        var msPerTick = histogram.MaxTicks == 0 ? 0 : stats.MaxMs / histogram.MaxTicks;
        var actual = histogram.PercentileTicks(percentile) * msPerTick;

        return Compare($"p{percentile.ToString("0.##", CultureInfo.InvariantCulture)}", actual, ms);
    }

    private ScopeAssertion Compare(string metric, double actual, double limit)
    {
        if (actual >= limit)
            throw Failed($"expected {_label} {metric} to be under {Format(limit)} ms, but it was {Format(actual)} ms");

        return this;
    }

    private TraceIdStats Require()
    {
        return _stats ?? throw Failed($"{_label} was never recorded in this trace");
    }

    private TraceAssertionException Failed(string message)
    {
        return new TraceAssertionException($"Trace assertion failed: {message}.");
    }

    private static string Format(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }
}
