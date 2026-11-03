using EmberTrace.Analysis.Analyzers;
using EmberTrace.Analysis.Model;
using EmberTrace.Analysis.Stats;
using EmberTrace.Sessions;

namespace EmberTrace;

public static class TraceSessionExtensions
{
    public static TraceStats Analyze(this TraceSession session, bool strict = false, int maxSamplesPerId = 100_000)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSamplesPerId);
        return StatsAnalyzer.Analyze(session, strict, maxSamplesPerId);
    }

    public static ProcessedTrace Process(this TraceSession session, bool strict = false, bool groupByThread = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        return CallTreeBuilder.Process(session, strict, groupByThread);
    }

    public static IReadOnlyList<FlowAnalysis> AnalyzeFlows(this TraceSession session, int top = 10)
    {
        ArgumentNullException.ThrowIfNull(session);
        return FlowAnalyzer.AnalyzeFlows(session, top);
    }
}