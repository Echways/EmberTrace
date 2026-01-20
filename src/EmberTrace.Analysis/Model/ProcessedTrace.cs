using System.Collections.Generic;

namespace EmberTrace.Analysis.Model;

public sealed class ProcessedTrace
{
    public required double DurationMs { get; init; }
    public required long TotalEvents { get; init; }
    public required int ThreadsSeen { get; init; }
    public required long MismatchedEndCount { get; init; }

    public required IReadOnlyList<ThreadTrace> Threads { get; init; }
    public required IReadOnlyList<HotspotRow> HotspotsByInclusiveDesc { get; init; }
}
