using System.Globalization;
using EmberTrace.Analysis.Stats;
using EmberTrace.Metadata;

namespace EmberTrace.Testing;

public static class TraceAssertions
{
    public static ScopeAssertion Scope(this TraceStats stats, int id, ITraceMetadataProvider? meta = null)
    {
        ArgumentNullException.ThrowIfNull(stats);

        TraceIdStats? match = null;
        var rows = stats.ByTotalTimeDesc;

        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Id != id)
                continue;

            match = rows[i];
            break;
        }

        return new ScopeAssertion(Describe(id, meta), match);
    }

    private static string Describe(int id, ITraceMetadataProvider? meta)
    {
        if (meta is not null && meta.TryGet(id, out var entry) && !string.IsNullOrEmpty(entry.Name))
            return $"'{entry.Name}' (id {id.ToString(CultureInfo.InvariantCulture)})";

        return $"id {id.ToString(CultureInfo.InvariantCulture)}";
    }
}
