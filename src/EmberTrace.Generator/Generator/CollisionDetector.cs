using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Generator;

internal static class CollisionDetector
{
    internal static void CheckAndRecord(int id, string name, Dictionary<int, string> seen, Location? location, SourceProductionContext spc)
    {
        if (seen.TryGetValue(id, out var existing))
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateId, location, id, existing));
        else
            seen.Add(id, name);
    }
}
