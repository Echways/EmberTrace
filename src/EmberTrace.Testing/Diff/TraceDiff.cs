using System.Globalization;
using System.Text;
using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;

namespace EmberTrace.Testing;

public static class TraceDiff
{
    public static TraceComparison Compare(TraceStats baseline, TraceStats current)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var baselineById = Index(baseline);
        var currentById = Index(current);

        var ids = new HashSet<int>(baselineById.Keys);
        ids.UnionWith(currentById.Keys);

        var deltas = new List<TraceIdDelta>(ids.Count);

        foreach (var id in ids)
        {
            baselineById.TryGetValue(id, out var before);
            currentById.TryGetValue(id, out var after);

            var inBoth = before is not null && after is not null;

            deltas.Add(new TraceIdDelta
            {
                Id = id,
                InBaseline = before is not null,
                InCurrent = after is not null,
                BaselineCount = before?.Count ?? 0,
                CurrentCount = after?.Count ?? 0,
                BaselineTotalMs = before?.TotalMs ?? 0,
                CurrentTotalMs = after?.TotalMs ?? 0,
                BaselineP95Ms = before?.P95Ms ?? 0,
                CurrentP95Ms = after?.P95Ms ?? 0,
                TotalMsChangePercent = inBoth ? ChangePercent(before!.TotalMs, after!.TotalMs) : double.NaN,
                P95MsChangePercent = inBoth ? ChangePercent(before!.P95Ms, after!.P95Ms) : double.NaN
            });
        }

        deltas.Sort(static (a, b) => Rank(b).CompareTo(Rank(a)));

        return new TraceComparison(deltas);
    }

    public static string Format(TraceComparison comparison, ITraceMetadataProvider? meta = null, double minPercent = 0)
    {
        ArgumentNullException.ThrowIfNull(comparison);

        var sb = new StringBuilder(4096);
        sb.AppendLine("Id      Name                 Base ms    Cur ms     Total%     Base p95   Cur p95    p95%");
        sb.AppendLine("------  -------------------  ---------  ---------  ---------  ---------  ---------  ---------");

        foreach (var d in comparison.Deltas)
        {
            if (minPercent > 0 && d.InBoth && d.TotalMsChangePercent < minPercent)
                continue;

            var name = ResolveName(meta, d.Id);
            if (!d.InBaseline) name += " (new)";
            else if (!d.InCurrent) name += " (gone)";

            sb.Append(d.Id.ToString(CultureInfo.InvariantCulture).PadRight(8));
            sb.Append(Truncate(name, 19).PadRight(21));
            sb.Append(Num(d.BaselineTotalMs).PadRight(11));
            sb.Append(Num(d.CurrentTotalMs).PadRight(11));
            sb.Append(Percent(d.TotalMsChangePercent).PadRight(11));
            sb.Append(Num(d.BaselineP95Ms).PadRight(11));
            sb.Append(Num(d.CurrentP95Ms).PadRight(11));
            sb.AppendLine(Percent(d.P95MsChangePercent));
        }

        return sb.ToString();
    }

    internal static double ChangePercent(double baseline, double current)
    {
        if (baseline == 0)
            return current == 0 ? 0 : double.PositiveInfinity;

        return (current - baseline) / baseline * 100.0;
    }

    private static double Rank(TraceIdDelta delta)
    {
        return double.IsNaN(delta.TotalMsChangePercent) ? double.NegativeInfinity : delta.TotalMsChangePercent;
    }

    private static Dictionary<int, TraceIdStats> Index(TraceStats stats)
    {
        var rows = stats.ByTotalTimeDesc;
        var map = new Dictionary<int, TraceIdStats>(rows.Count);

        for (var i = 0; i < rows.Count; i++)
            map[rows[i].Id] = rows[i];

        return map;
    }

    private static string ResolveName(ITraceMetadataProvider? meta, int id)
    {
        if (meta is not null && meta.TryGet(id, out var entry) && !string.IsNullOrEmpty(entry.Name))
            return entry.Name;

        return "-";
    }

    private static string Truncate(string value, int max)
    {
        return value.Length <= max ? value : value[..max];
    }

    private static string Num(double value)
    {
        return value.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static string Percent(double value)
    {
        if (double.IsNaN(value)) return "-";
        if (double.IsPositiveInfinity(value)) return "+inf";

        return (value >= 0 ? "+" : "") + value.ToString("F1", CultureInfo.InvariantCulture);
    }
}
