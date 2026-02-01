using System.Collections.Generic;

namespace EmberTrace.Analysis.Model;

public sealed class ProcessedTrace
{
    public required double DurationMs { get; init; }
    public required long TotalEvents { get; init; }
    public required int ThreadsSeen { get; init; }
    public required long UnmatchedBeginCount { get; init; }
    public required long UnmatchedEndCount { get; init; }
    public required long MismatchedEndCount { get; init; }
    public required long DroppedEvents { get; init; }
    public required long DroppedChunks { get; init; }
    public required long SampledOutEvents { get; init; }
    public required bool WasOverflow { get; init; }

    public required IReadOnlyList<ThreadTrace> Threads { get; init; }
    public required CallTreeNode GlobalRoot { get; init; }
    public required IReadOnlyList<HotspotRow> HotspotsByInclusiveDesc { get; init; }
}
