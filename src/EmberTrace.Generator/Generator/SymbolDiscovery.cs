using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace EmberTrace.Generator.Generator;

internal static class SymbolDiscovery
{
    internal static List<TraceItem> Collect(Compilation compilation, SourceProductionContext spc)
    {
        var list = new List<TraceItem>();
        var seen = new Dictionary<int, string>();

        foreach (var attr in compilation.Assembly.GetAttributes())
        {
            var a = attr.AttributeClass;
            if (a is null) continue;

            var fullName = a.ToDisplayString();
            if (fullName != "EmberTrace.Abstractions.Attributes.TraceIdAttribute"
                && fullName != "EmberTrace.Abstractions.TraceIdAttribute")
                continue;

            if (attr.ConstructorArguments.Length < 2)
                continue;

            var id = (int)attr.ConstructorArguments[0].Value!;
            var name = attr.ConstructorArguments[1].Value as string ?? string.Empty;

            var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation();
            if (string.IsNullOrWhiteSpace(name))
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.EmptyName, location, id));

            string? category = null;
            if (attr.ConstructorArguments.Length >= 3)
                category = attr.ConstructorArguments[2].Value as string;

            if (category is not null && string.IsNullOrWhiteSpace(category))
                spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.EmptyCategory, location, id));

            CollisionDetector.CheckAndRecord(id, name, seen, location, spc);
            list.Add(new TraceItem(id, name, category, location));
        }

        return list;
    }
}

internal readonly struct TraceItem
{
    public TraceItem(int id, string name, string? category, Location? location)
    {
        Id = id;
        Name = name;
        Category = category;
        Location = location;
    }

    public int Id { get; }
    public string Name { get; }
    public string? Category { get; }
    public Location? Location { get; }
}
