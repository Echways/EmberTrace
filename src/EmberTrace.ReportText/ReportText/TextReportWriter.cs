using System.Text;
using EmberTrace.Analysis.Model;
using EmberTrace.Metadata;

namespace EmberTrace.ReportText;

internal static class TextReportWriter
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
        var sb = new StringBuilder(32_768);

        sb.AppendLine("Summary");
        sb.AppendLine($"Duration: {trace.DurationMs:F3} ms");
        sb.AppendLine($"Events: {trace.TotalEventCount}");
        sb.AppendLine($"ScopeEvents: {trace.ScopeEventCount}");
        sb.AppendLine($"Threads: {trace.ThreadsSeen}");
        sb.AppendLine($"DroppedEvents: {trace.DroppedEvents}");
        sb.AppendLine($"DroppedChunks: {trace.DroppedChunks}");
        sb.AppendLine($"SampledOut: {trace.SampledOutEvents}");
        sb.AppendLine($"UnmatchedBegin: {trace.UnmatchedBeginCount}");
        sb.AppendLine($"UnmatchedEnd: {trace.UnmatchedEndCount}");
        sb.AppendLine($"MismatchedEnd: {trace.MismatchedEndCount}");
        sb.AppendLine();

        WriteHotspots(sb, trace, meta, topHotspots, categoryFilter, minPercent, includePercentiles);
        sb.AppendLine();
        if (WriteCategoryGroups(sb, trace, meta, categoryFilter))
            sb.AppendLine();
        WriteThreads(sb, trace, meta, maxDepth, categoryFilter, minPercent);

        return sb.ToString();
    }

    private static void WriteHotspots(
        StringBuilder sb,
        ProcessedTrace trace,
        ITraceMetadataProvider? meta,
        int top,
        string? categoryFilter,
        double minPercent,
        bool includePercentiles)
    {
        sb.AppendLine("Hotspots (by inclusive)");

        var t = includePercentiles
            ? new TextTable("Id", "Name", "Category", "Count", "Incl ms", "Excl ms", "Excl%", "p50 ms", "p95 ms", "p99 ms")
            : new TextTable("Id", "Name", "Category", "Count", "Incl ms", "Excl ms", "Excl%");

        t.AddSeparator();

        var list = trace.HotspotsByInclusiveDesc;
        var n = Math.Min(top, list.Count);

        for (var i = 0; i < n; i++)
        {
            var r = list[i];
            var exclPct = trace.DurationMs <= 0 ? 0 : r.ExclusiveMs / trace.DurationMs * 100.0;
            var inclPct = trace.DurationMs <= 0 ? 0 : r.InclusiveMs / trace.DurationMs * 100.0;

            meta.Resolve(r.Id, out var name, out var cat);
            if (!MatchesCategory(categoryFilter, cat))
                continue;
            if (minPercent > 0 && inclPct < minPercent)
                continue;

            if (includePercentiles)
                t.AddRow(
                    r.Id.ToString(),
                    name,
                    cat,
                    r.Count.ToString(),
                    r.InclusiveMs.ToString("F3"),
                    r.ExclusiveMs.ToString("F3"),
                    exclPct.ToString("F2"),
                    r.P50Ms.ToString("F3"),
                    r.P95Ms.ToString("F3"),
                    r.P99Ms.ToString("F3"));
            else
                t.AddRow(
                    r.Id.ToString(),
                    name,
                    cat,
                    r.Count.ToString(),
                    r.InclusiveMs.ToString("F3"),
                    r.ExclusiveMs.ToString("F3"),
                    exclPct.ToString("F2"));
        }

        t.WriteTo(sb);
    }

    private static void WriteThreads(
        StringBuilder sb,
        ProcessedTrace trace,
        ITraceMetadataProvider? meta,
        int maxDepth,
        string? categoryFilter,
        double minPercent)
    {
        sb.AppendLine("Call trees");

        for (var i = 0; i < trace.Threads.Count; i++)
        {
            var th = trace.Threads[i];
            sb.AppendLine();
            sb.AppendLine($"Thread {th.ThreadId}");

            var t = new TextTable("Id", "Name", "Category", "Count", "Incl ms", "Excl ms");
            t.AddSeparator();

            for (var c = 0; c < th.Root.Children.Count; c++)
                WriteNode(t, th.Root.Children[c], meta, 0, maxDepth, trace.DurationMs, categoryFilter, minPercent);

            t.WriteTo(sb);
        }
    }

    private static void WriteNode(
        TextTable t,
        CallTreeNode node,
        ITraceMetadataProvider? meta,
        int depth,
        int maxDepth,
        double totalMs,
        string? categoryFilter,
        double minPercent)
    {
        var id = depth == 0 ? node.Id.ToString() : new string(' ', depth * 2) + node.Id;

        meta.Resolve(node.Id, out var name, out var cat);
        if (!MatchesCategory(categoryFilter, cat))
            return;

        if (minPercent > 0 && totalMs > 0)
        {
            var inclPct = node.InclusiveMs / totalMs * 100.0;
            if (inclPct < minPercent)
                return;
        }

        t.AddRow(
            id,
            name,
            cat,
            node.Count.ToString(),
            node.InclusiveMs.ToString("F3"),
            node.ExclusiveMs.ToString("F3"));

        if (depth + 1 >= maxDepth)
            return;

        for (var i = 0; i < node.Children.Count; i++)
            WriteNode(t, node.Children[i], meta, depth + 1, maxDepth, totalMs, categoryFilter, minPercent);
    }

    private static bool WriteCategoryGroups(
        StringBuilder sb,
        ProcessedTrace trace,
        ITraceMetadataProvider? meta,
        string? categoryFilter)
    {
        if (meta is null)
            return false;

        var byCat = new Dictionary<string, (long Count, double InclMs, double ExclMs)>(StringComparer.Ordinal);

        for (var i = 0; i < trace.HotspotsByInclusiveDesc.Count; i++)
        {
            var r = trace.HotspotsByInclusiveDesc[i];
            meta.Resolve(r.Id, out _, out var cat);
            if (string.IsNullOrEmpty(cat))
                continue;
            if (!MatchesCategory(categoryFilter, cat))
                continue;

            if (byCat.TryGetValue(cat, out var agg))
                byCat[cat] = (agg.Count + r.Count, agg.InclMs + r.InclusiveMs, agg.ExclMs + r.ExclusiveMs);
            else
                byCat[cat] = (r.Count, r.InclusiveMs, r.ExclusiveMs);
        }

        if (byCat.Count == 0)
            return false;

        sb.AppendLine("Categories (by inclusive)");
        var t = new TextTable("Category", "Count", "Incl ms", "Excl ms", "Excl%");
        t.AddSeparator();

        foreach (var kv in byCat.OrderByDescending(static x => x.Value.InclMs))
        {
            var cat = kv.Key;
            var agg = kv.Value;
            var exclPct = trace.DurationMs <= 0 ? 0 : agg.ExclMs / trace.DurationMs * 100.0;
            t.AddRow(
                cat,
                agg.Count.ToString(),
                agg.InclMs.ToString("F3"),
                agg.ExclMs.ToString("F3"),
                exclPct.ToString("F2"));
        }

        t.WriteTo(sb);
        return true;
    }

    private static bool MatchesCategory(string? filter, string category)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return string.Equals(filter, category, StringComparison.OrdinalIgnoreCase);
    }

}