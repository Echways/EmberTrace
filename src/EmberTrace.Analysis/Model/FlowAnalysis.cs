using System.Collections.Generic;
using EmberTrace.Sessions;

namespace EmberTrace.Analysis.Model;

public sealed class FlowAnalysis
{
    public required long FlowId { get; init; }
    public required int Id { get; init; }
    public required long StartTimestamp { get; init; }
    public required long EndTimestamp { get; init; }
    public required double TotalDurationMs { get; init; }
    public required IReadOnlyList<FlowStepInfo> Steps { get; init; }
}

public sealed class FlowStepInfo
{
    public required int Id { get; init; }
    public required TraceEventKind Kind { get; init; }
    public required long Timestamp { get; init; }
    public required double DurationMs { get; init; }
}
