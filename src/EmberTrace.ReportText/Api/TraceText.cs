using EmberTrace.Analysis.Calibration;
using EmberTrace.Analysis.Compare;
using EmberTrace.Analysis.Model;
using EmberTrace.Metadata;
using EmberTrace.ReportText;

namespace EmberTrace;

public static class TraceText
{
    public static string Write(
        ProcessedTrace trace,
        ITraceMetadataProvider? meta = null,
        int topHotspots = 10,
        int maxDepth = 3,
        string? categoryFilter = null,
        double minPercent = 0,
        bool includePercentiles = false,
        OverheadReport? overhead = null)
    {
        return TextReportWriter.Write(
            trace, meta ?? trace.Metadata, topHotspots, maxDepth, categoryFilter, minPercent, includePercentiles,
            overhead);
    }

    public static void WriteCollapsedStacks(ProcessedTrace trace, TextWriter output, ITraceMetadataProvider? meta = null)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(output);

        CollapsedStackWriter.Write(trace, output, meta ?? trace.Metadata);
    }

    public static void WriteScopeTable(
        IEnumerable<TraceRun> runs,
        TextWriter writer,
        ITraceMetadataProvider? meta = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(writer);

        ScopeTableWriter.Write(runs, writer, meta);
    }
}
