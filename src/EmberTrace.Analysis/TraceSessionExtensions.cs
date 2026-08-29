using EmberTrace.Analysis.Analyzers;
using EmberTrace.Analysis.Model;
using EmberTrace.Analysis.Stats;
using EmberTrace.Sessions;

namespace EmberTrace;

public static class TraceSessionExtensions
{
    public static TraceStats Analyze(this TraceSession session, bool strict = false)
    {
        ArgumentNullException.ThrowIfNull(session);
        return StatsAnalyzer.Analyze(session, strict);
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
