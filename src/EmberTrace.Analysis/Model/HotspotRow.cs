using EmberTrace.Analysis.Stats;

namespace EmberTrace.Analysis.Model;

public sealed class HotspotRow
{
    public required int Id { get; init; }
    public required long Count { get; init; }
    public required double InclusiveMs { get; init; }
    public required double ExclusiveMs { get; init; }
    public DurationHistogram? Durations { get; init; }
    public double P50Ms { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
}
