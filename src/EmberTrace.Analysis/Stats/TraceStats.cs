namespace EmberTrace.Analysis.Stats;

public sealed class TraceStats
{
    public required double DurationMs { get; init; }
    public required long TotalEventCount { get; init; }
    public required long ScopeEventCount { get; init; }
    public required int ThreadsSeen { get; init; }
    public required long UnmatchedBeginCount { get; init; }
    public required long UnmatchedEndCount { get; init; }
    public required long MismatchedEndCount { get; init; }
    public required IReadOnlyList<TraceIdStats> ByTotalTimeDesc { get; init; }
}