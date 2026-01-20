using EmberTrace.Analysis.Model;
using EmberTrace.Metadata;

namespace EmberTrace;

public static class TraceText
{
    public static string Write(
        ProcessedTrace trace,
        ITraceMetadataProvider? meta = null,
        int topHotspots = 10,
        int maxDepth = 3)
    {
        return ReportText.TextReportWriter.Write(trace, meta, topHotspots, maxDepth);
    }
}
