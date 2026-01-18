using System;
using System.Text;
using EmberTrace.Processing.Model;
using EmberTrace.Internal.Metadata;
using EmberTrace.Public;

namespace EmberTrace.Reporting.Text;

public static class TextReportWriter
{
    public static string Write(
        ProcessedTrace trace,
        ITraceMetadataProvider? meta = null,
        int topHotspots = 10,
        int maxDepth = 3)
    {
        var sb = new StringBuilder(32_768);

        sb.AppendLine("Summary");
        sb.AppendLine($"Duration: {trace.DurationMs:F3} ms");
        sb.AppendLine($"Events: {trace.TotalEvents}");
        sb.AppendLine($"Threads: {trace.ThreadsSeen}");
        sb.AppendLine($"MismatchedEnd: {trace.MismatchedEndCount}");
        sb.AppendLine();

        WriteHotspots(sb, trace, meta, topHotspots);
        sb.AppendLine();
        WriteThreads(sb, trace, meta, maxDepth);

        return sb.ToString();
    }

    private static void WriteHotspots(StringBuilder sb, ProcessedTrace trace, ITraceMetadataProvider? meta, int top)
    {
        sb.AppendLine("Hotspots (by inclusive)");
        var t = new TextTable("Id", "Name", "Category", "Count", "Incl ms", "Excl ms", "Excl%");

        t.AddSeparator();

        var list = trace.HotspotsByInclusiveDesc;
        var n = Math.Min(top, list.Count);

        for (int i = 0; i < n; i++)
        {
            var r = list[i];
            var exclPct = trace.DurationMs <= 0 ? 0 : (r.ExclusiveMs / trace.DurationMs) * 100.0;

            Resolve(meta, r.Id, out var name, out var cat);

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

    private static void WriteThreads(StringBuilder sb, ProcessedTrace trace, ITraceMetadataProvider? meta, int maxDepth)
    {
        sb.AppendLine("Call trees");

        for (int i = 0; i < trace.Threads.Count; i++)
        {
            var th = trace.Threads[i];
            sb.AppendLine();
            sb.AppendLine($"Thread {th.ThreadId}");

            var t = new TextTable("Id", "Name", "Category", "Count", "Incl ms", "Excl ms");
            t.AddSeparator();

            for (int c = 0; c < th.Root.Children.Count; c++)
                WriteNode(t, th.Root.Children[c], meta, depth: 0, maxDepth);

            t.WriteTo(sb);
        }
    }

    private static void WriteNode(TextTable t, CallTreeNode node, ITraceMetadataProvider? meta, int depth, int maxDepth)
    {
        var id = depth == 0 ? node.Id.ToString() : new string(' ', depth * 2) + node.Id;

        Resolve(meta, node.Id, out var name, out var cat);

        t.AddRow(
            id,
            name,
            cat,
            node.Count.ToString(),
            node.InclusiveMs.ToString("F3"),
            node.ExclusiveMs.ToString("F3"));

        if (depth + 1 >= maxDepth)
            return;

        for (int i = 0; i < node.Children.Count; i++)
            WriteNode(t, node.Children[i], meta, depth + 1, maxDepth);
    }

    private static void Resolve(ITraceMetadataProvider? meta, int id, out string name, out string category)
    {
        if (meta is not null && meta.TryGet(id, out var m))
        {
            name = m.Name;
            category = m.Category ?? "";
            return;
        }

        name = "";
        category = "";
    }
}
