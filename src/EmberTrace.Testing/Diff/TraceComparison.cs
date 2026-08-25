namespace EmberTrace.Testing;

public sealed class TraceComparison
{
    internal TraceComparison(IReadOnlyList<TraceIdDelta> deltas)
    {
        Deltas = deltas;

        var inBoth = new List<TraceIdDelta>(deltas.Count);
        for (var i = 0; i < deltas.Count; i++)
            if (deltas[i].InBoth)
                inBoth.Add(deltas[i]);

        InBothOnly = inBoth;
    }

    public IReadOnlyList<TraceIdDelta> Deltas { get; }
    public IReadOnlyList<TraceIdDelta> InBothOnly { get; }

    public IEnumerable<TraceIdDelta> RegressionsOver(double percent)
    {
        foreach (var delta in InBothOnly)
            if (delta.TotalMsChangePercent > percent)
                yield return delta;
    }
}
