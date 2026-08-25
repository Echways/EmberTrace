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
        bool includePercentiles = false)
    {
        return TextReportWriter.Write(
            trace, meta ?? trace.Metadata, topHotspots, maxDepth, categoryFilter, minPercent, includePercentiles);
    }
}