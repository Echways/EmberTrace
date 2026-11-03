using System.Globalization;
using EmberTrace.Analysis.Compare;
using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;

namespace EmberTrace.ReportText;

internal static class ScopeTableWriter
{
    private const string Header =
        "run,arm,round,id,name,count,total_ms,mean_ms,min_ms,p25_ms,p50_ms,p75_ms,p95_ms,p99_ms,max_ms,exact,bimodal,overflow,dropped";

    private const string LineEnd = "\r\n";

    public static void Write(IEnumerable<TraceRun> runs, TextWriter writer, ITraceMetadataProvider? meta)
    {
        writer.Write(Header + LineEnd);

        foreach (var run in runs)
            foreach (var row in run.Stats.ByTotalTimeDesc)
                WriteRow(run, row, meta ?? run.Metadata, writer);
    }

    private static void WriteRow(TraceRun run, TraceIdStats row, ITraceMetadataProvider? meta, TextWriter writer)
    {
        meta.Resolve(row.Id, out var name, out _);

        string[] fields =
        [
            Quote(run.Label),
            Quote(run.Arm),
            Number(run.Round),
            Number(row.Id),
            Quote(name),
            Number(row.Count),
            Number(row.TotalMs),
            Number(row.AverageMs),
            Number(row.MinMs),
            row.Shape is { } shape ? Number(shape.P25Ms) : string.Empty,
            Number(row.P50Ms),
            row.Shape is { } upper ? Number(upper.P75Ms) : string.Empty,
            Number(row.P95Ms),
            Number(row.P99Ms),
            Number(row.MaxMs),
            Flag(row.IsExact),
            row.Shape is { } modal ? Flag(modal.IsBimodal) : string.Empty,
            Flag(run.WasOverflow),
            Number(run.DroppedEvents)
        ];

        writer.Write(string.Join(',', fields) + LineEnd);
    }

    private static string Number(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string Flag(bool value)
    {
        return value ? "true" : "false";
    }

    private static string Quote(string value)
    {
        return value.AsSpan().IndexOfAny(",\"\r\n") < 0
            ? value
            : "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
