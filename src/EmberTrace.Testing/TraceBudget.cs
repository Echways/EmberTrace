using System.Globalization;
using System.Text;
using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;

namespace EmberTrace.Testing;

public static class TraceBudget
{
    public static void AssertNoRegressions(
        TraceStats baseline,
        TraceStats current,
        double maxPercent,
        ITraceMetadataProvider? meta = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);

        var comparison = TraceDiff.Compare(baseline, current);

        List<TraceIdDelta>? offenders = null;
        foreach (var delta in comparison.RegressionsOver(maxPercent))
            (offenders ??= new List<TraceIdDelta>()).Add(delta);

        if (offenders is null)
            return;

        var sb = new StringBuilder(1024);
        sb.Append("Trace assertion failed: ")
            .Append(offenders.Count.ToString(CultureInfo.InvariantCulture))
            .Append(offenders.Count == 1 ? " scope regressed" : " scopes regressed")
            .Append(" by more than ")
            .Append(maxPercent.ToString("F1", CultureInfo.InvariantCulture))
            .AppendLine("%:");

        foreach (var delta in offenders)
        {
            var name = meta is not null && meta.TryGet(delta.Id, out var entry) && !string.IsNullOrEmpty(entry.Name)
                ? $"'{entry.Name}' (id {delta.Id.ToString(CultureInfo.InvariantCulture)})"
                : $"id {delta.Id.ToString(CultureInfo.InvariantCulture)}";

            sb.Append("  ")
                .Append(name)
                .Append(": ")
                .Append(delta.BaselineTotalMs.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" ms -> ")
                .Append(delta.CurrentTotalMs.ToString("F3", CultureInfo.InvariantCulture))
                .Append(" ms (")
                .Append(delta.TotalMsChangePercent >= 0 ? "+" : "")
                .Append(delta.TotalMsChangePercent.ToString("F1", CultureInfo.InvariantCulture))
                .AppendLine("%)");
        }

        throw new TraceAssertionException(sb.ToString());
    }
}
