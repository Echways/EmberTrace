using System.Collections.Generic;

namespace EmberTrace.Reporting;

public sealed class TraceStats
{
    public required double DurationMs { get; init; }
    public required long TotalEvents { get; init; }
    public required int ThreadsSeen { get; init; }
    public required long MismatchedEndCount { get; init; }
    public required IReadOnlyList<TraceIdStats> ByTotalTimeDesc { get; init; }
}