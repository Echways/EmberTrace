using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace EmberTrace.Generator.Generator;

internal readonly record struct LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    internal static LocationInfo? From(SyntaxNode? node)
    {
        return node is null
            ? null
            : new LocationInfo(node.SyntaxTree.FilePath, node.Span, node.GetLocation().GetLineSpan().Span);
    }

    internal Location ToLocation()
    {
        return Location.Create(FilePath, TextSpan, LineSpan);
    }
}

internal readonly record struct TraceItem(int Id, string? Name, string? Category, LocationInfo? Location)
{
    internal static readonly IComparer<TraceItem> Ordering = Comparer<TraceItem>.Create(static (a, b) =>
    {
        var order = a.Id.CompareTo(b.Id);
        if (order != 0)
            return order;

        order = string.CompareOrdinal(a.Name, b.Name);
        if (order != 0)
            return order;

        order = string.CompareOrdinal(a.Location?.FilePath, b.Location?.FilePath);
        return order != 0
            ? order
            : (a.Location?.TextSpan.Start ?? 0).CompareTo(b.Location?.TextSpan.Start ?? 0);
    });

    internal Location? Origin => Location?.ToLocation();

    internal static TraceItem Malformed(LocationInfo? location)
    {
        return new TraceItem(0, null, null, location);
    }
}

internal readonly record struct TraceConstant(string Name, int Id);