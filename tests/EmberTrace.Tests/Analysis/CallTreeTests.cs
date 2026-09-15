using EmberTrace.Analysis.Model;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Analysis;

[TestClass]
public class CallTreeTests
{
    private const int Request = 1;
    private const int Query = 2;
    private const int Parse = 3;

    [TestMethod]
    public void Process_SplitsInclusiveAndExclusiveTimeAcrossTheTree()
    {
        var trace = Script().ToSession().Process();

        var request = Child(trace.Threads.Single(t => t.ThreadId == 1).Root, Request);
        var query = Child(request, Query);
        var parse = Child(query, Parse);

        AssertNode(request, 1, 10.0, 5.0);
        AssertNode(query, 2, 5.0, 4.0);
        AssertNode(parse, 1, 1.0, 1.0);
        CollectionAssert.AreEqual(new[] { Query }, request.Children.Select(c => c.Id).ToArray());
    }

    [TestMethod]
    public void Process_GroupsByTrackOrMergesIntoOneGlobalTree()
    {
        var session = Script().ToSession(threadNames: new Dictionary<int, string> { [2] = "worker" });

        var grouped = session.Process();
        var merged = session.Process(groupByThread: false);

        CollectionAssert.AreEqual(new[] { (1, (string?)null), (2, "worker") },
            grouped.Threads.Select(t => (t.ThreadId, t.Name)).ToArray());
        AssertNode(Child(grouped.Threads[1].Root, Request), 1, 2.0, 2.0);

        var global = merged.Threads.Single();
        Assert.AreEqual(0, global.ThreadId);
        Assert.AreSame(merged.GlobalRoot, global.Root);
        AssertNode(Child(merged.GlobalRoot, Request), 2, 12.0, 7.0);
        Assert.AreEqual(2, merged.ThreadsSeen);
    }

    [TestMethod]
    public void Process_RanksChildrenAndHotspotsByInclusiveTime()
    {
        var trace = new TraceScript()
            .Span(Parse, 0, 1_000)
            .Span(Query, 1_000, 4_000)
            .Span(Parse, 4_000, 5_000)
            .Span(Request, 5_000, 7_500)
            .ToSession()
            .Process();

        CollectionAssert.AreEqual(new[] { Query, Request, Parse },
            trace.GlobalRoot.Children.Select(c => c.Id).ToArray());
        CollectionAssert.AreEqual(new[] { (Query, 1L), (Request, 1L), (Parse, 2L) },
            trace.HotspotsByInclusiveDesc.Select(h => (h.Id, h.Count)).ToArray());
        Assert.AreEqual(2.0, trace.HotspotsByInclusiveDesc[2].InclusiveMs, 1e-9);
    }

    [TestMethod]
    public void Process_RecycledThreadId_KeepsCallTreesApartButReportsTheManagedThread()
    {
        var trace = TraceSession.FromEvents(
        [
            new(1, 7, 10, TraceEventKind.Begin, 0, 0, 1, 1),
            new(1, 7, 20, TraceEventKind.End, 0, 0, 2, 1),
            new(2, 7, 30, TraceEventKind.Begin, 0, 0, 1, 2),
            new(2, 7, 40, TraceEventKind.End, 0, 0, 2, 2)
        ], 0, 40, 1_000_000).Process();

        CollectionAssert.AreEqual(new[] { (1, 7, 1), (2, 7, 2) },
            trace.Threads.Select(t => (t.TrackId, t.ThreadId, t.Root.Children.Single().Id)).ToArray());
    }

    [TestMethod]
    public void Process_CarriesSessionCounters()
    {
        var session = TraceSession.FromEvents([], 0, 2_000, 1_000_000,
            droppedEvents: 3, droppedChunks: 2, sampledOutEvents: 5, wasOverflow: true);

        var trace = session.Process();

        Assert.AreEqual(2.0, trace.DurationMs, 1e-9);
        Assert.AreEqual(3L, trace.DroppedEvents);
        Assert.AreEqual(2L, trace.DroppedChunks);
        Assert.AreEqual(5L, trace.SampledOutEvents);
        Assert.IsTrue(trace.WasOverflow);
        Assert.IsEmpty(trace.HotspotsByInclusiveDesc);
        Assert.IsEmpty(trace.GlobalRoot.Children);
    }

    private static TraceScript Script()
    {
        return new TraceScript()
            .Begin(Request, 0)
            .Begin(Query, 1_000)
            .Span(Parse, 2_000, 3_000)
            .End(Query, 3_000)
            .Span(Query, 4_000, 7_000)
            .End(Request, 10_000)
            .Span(Request, 0, 2_000, 2);
    }

    private static CallTreeNode Child(CallTreeNode node, int id)
    {
        return node.Children.Single(c => c.Id == id);
    }

    private static void AssertNode(CallTreeNode node, long count, double inclusiveMs, double exclusiveMs)
    {
        Assert.AreEqual(count, node.Count);
        Assert.AreEqual(inclusiveMs, node.InclusiveMs, 1e-9);
        Assert.AreEqual(exclusiveMs, node.ExclusiveMs, 1e-9);
    }
}
