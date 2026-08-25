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
}
