using EmberTrace.Internal.Time;

namespace EmberTrace.Analysis.Stats;

public sealed class TraceIdStats
{
    public required int Id { get; init; }
    public required long Count { get; init; }
    public required double TotalMs { get; init; }
    public required double AverageMs { get; init; }
    public required double MinMs { get; init; }
    public required double MaxMs { get; init; }
    public DurationHistogram? Durations { get; init; }
    public double P50Ms { get; init; }
    public double P90Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
    public ReadOnlyMemory<long> Samples { get; init; }
    public bool IsExact { get; init; }
    public DistributionShape? Shape { get; init; }

    public WarmupCurve Warmup(int windows = 10)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windows);

        if (Samples.IsEmpty)
            return new WarmupCurve([]);

        var frequency = Durations?.TimestampFrequency ?? 0;
        if (frequency == 0)
            throw new InvalidOperationException(
                "The statistics carry no timestamp frequency; produce them with Analyze().");

        var conv = new TickConverter(frequency);
        return WarmupCurve.FromSamples(Samples.Span, windows, conv.ToMs);
    }
}
