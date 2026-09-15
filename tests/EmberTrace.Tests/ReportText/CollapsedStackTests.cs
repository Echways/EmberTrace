using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.ReportText;

[TestClass]
public class CollapsedStackTests
{
    [TestMethod]
    public void NestedScopes_ProduceOneLinePerPathWithExclusiveMicroseconds()
    {
        var lines = Collapse(Meta((1, "Outer"), (2, "Inner")),
            new(1, 1, 0, TraceEventKind.Begin, 0, 0, 1),
            new(2, 1, 1_000, TraceEventKind.Begin, 0, 0, 2),
            new(2, 1, 5_000, TraceEventKind.End, 0, 0, 3),
            new(1, 1, 10_000, TraceEventKind.End, 0, 0, 4));

        Assert.AreEqual(6_000, lines["Outer"]);
        Assert.AreEqual(4_000, lines["Outer;Inner"]);
        Assert.HasCount(2, lines);
    }

    [TestMethod]
    public void SamePathOnDifferentThreads_IsMerged()
    {
        var lines = Collapse(Meta((1, "Work")),
            new(1, 1, 0, TraceEventKind.Begin, 0, 0, 1),
            new(1, 1, 1_000, TraceEventKind.End, 0, 0, 2),
            new(1, 2, 0, TraceEventKind.Begin, 0, 0, 1),
            new(1, 2, 3_000, TraceEventKind.End, 0, 0, 2));

        Assert.AreEqual(4_000, lines["Work"]);
    }

    [TestMethod]
    public void FrameNames_CannotBreakTheFormat()
    {
        var lines = Collapse(Meta((1, "a;b\nc")),
            new(1, 1, 0, TraceEventKind.Begin, 0, 0, 1),
            new(1, 1, 1_000, TraceEventKind.End, 0, 0, 2));

        Assert.AreEqual(1_000, lines["a:b c"]);
    }

    [TestMethod]
    public void FullyCoveredParent_IsOmitted_AndLinesEndWithLineFeed()
    {
        var trace = TraceSession.FromEvents(
        [
            new(1, 1, 0, TraceEventKind.Begin, 0, 0, 1),
            new(2, 1, 0, TraceEventKind.Begin, 0, 0, 2),
            new(2, 1, 2_000, TraceEventKind.End, 0, 0, 3),
            new(1, 1, 2_000, TraceEventKind.End, 0, 0, 4)
        ], 0, 2_000, 1_000_000).Process();

        using var writer = new StringWriter();
        TraceText.WriteCollapsedStacks(trace, writer, Meta((1, "Outer"), (2, "Inner")));

        Assert.AreEqual("Outer;Inner 2000\n", writer.ToString());
    }

    private static Dictionary<string, long> Collapse(ITraceMetadataProvider meta, params TraceEventRecord[] events)
    {
        var end = events.Max(e => e.Timestamp);
        var trace = TraceSession.FromEvents(events, 0, end, 1_000_000).Process();

        using var writer = new StringWriter();
        TraceText.WriteCollapsedStacks(trace, writer, meta);

        var lines = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var line in writer.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.LastIndexOf(' ');
            lines.Add(line[..separator], long.Parse(line[(separator + 1)..]));
        }

        return lines;
    }

    private static ITraceMetadataProvider Meta(params (int Id, string Name)[] entries)
    {
        return TraceMetadata.FromEntries(entries.Select(e => new TraceMeta(e.Id, e.Name, null)));
    }
}
