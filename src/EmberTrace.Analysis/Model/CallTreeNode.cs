using System.Collections.Generic;

namespace EmberTrace.Analysis.Model;

public sealed class CallTreeNode
{
    public required int Id { get; init; }
    public required long Count { get; init; }
    public required double InclusiveMs { get; init; }
    public required double ExclusiveMs { get; init; }
    public required IReadOnlyList<CallTreeNode> Children { get; init; }
}
