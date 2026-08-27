using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Generator;

internal static class TraceCatalog
{
    internal static ImmutableArray<TraceItem> Validate(
        EquatableArray<TraceItem> items,
        DiagnosticDescriptor malformed,
        SourceProductionContext spc)
    {
        var builder = ImmutableArray.CreateBuilder<TraceItem>();

        foreach (var item in items)
            if (item.Name is null)
                spc.ReportDiagnostic(Diagnostic.Create(malformed, item.Origin));
            else
                builder.Add(item);

        builder.Sort(TraceItem.Ordering);
        return builder.ToImmutable();
    }

    internal static void ReportContentIssues(ImmutableArray<TraceItem> items, SourceProductionContext spc)
    {
        var owners = new Dictionary<int, string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (!seen.Add(item.Id + "|" + item.Name + "|" + item.Category))
                continue;

            if (string.IsNullOrWhiteSpace(item.Name))
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.EmptyName, item.Origin, item.Id));

            if (item.Category is not null && string.IsNullOrWhiteSpace(item.Category))
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.EmptyCategory, item.Origin, item.Id));

            if (owners.TryGetValue(item.Id, out var existing))
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateId, item.Origin, item.Id, existing));
            else
                owners.Add(item.Id, item.Name!);
        }
    }
}