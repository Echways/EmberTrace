using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Analysis;

[TestClass]
public class TraceAnalysisTests
{
    [TestMethod]
    public void AnalyzeFlows_ComputesDurations()
    {
        var freq = Timestamp.Frequency;
        var start = 100L;
        var step = start + freq;
        var end = start + freq * 3;

        var session = CreateSession(new[]
        {
            new TraceEvent(1, 1, start, TraceEventKind.FlowStart, 42, 0),
            new TraceEvent(1, 1, step, TraceEventKind.FlowStep, 42, 0),
            new TraceEvent(1, 1, end, TraceEventKind.FlowEnd, 42, 0)
        });

        var flows = session.AnalyzeFlows();

        Assert.HasCount(1, flows);
        var flow = flows[0];
        Assert.AreEqual(42, flow.FlowId);
        Assert.HasCount(2, flow.Steps);
        Assert.AreEqual(3000.0, flow.TotalDurationMs, 0.01);
        Assert.AreEqual(1000.0, flow.Steps[0].DurationMs, 0.01);
        Assert.AreEqual(2000.0, flow.Steps[1].DurationMs, 0.01);
    }

    [TestMethod]
    public void Analyze_StrictMode_TracksMismatches()
    {
        var session = CreateSession(new[]
        {
            new TraceEvent(1, 1, 10, TraceEventKind.Begin, 0, 0),
            new TraceEvent(2, 1, 20, TraceEventKind.Begin, 0, 0),
            new TraceEvent(1, 1, 30, TraceEventKind.End, 0, 0),
            new TraceEvent(2, 1, 40, TraceEventKind.End, 0, 0),
            new TraceEvent(3, 1, 50, TraceEventKind.End, 0, 0)
        });

        var nonStrict = session.Analyze(false);
        Assert.AreEqual(1, nonStrict.MismatchedEndCount);
        Assert.AreEqual(1, nonStrict.UnmatchedBeginCount);
        Assert.AreEqual(2, nonStrict.UnmatchedEndCount);

        var strict = session.Analyze(true);
        Assert.AreEqual(2, strict.MismatchedEndCount);
        Assert.AreEqual(1, strict.UnmatchedBeginCount);
        Assert.AreEqual(0, strict.UnmatchedEndCount);

        var processed = session.Process(true);
        Assert.AreEqual(strict.MismatchedEndCount, processed.MismatchedEndCount);
        Assert.AreEqual(strict.UnmatchedBeginCount, processed.UnmatchedBeginCount);
        Assert.AreEqual(strict.UnmatchedEndCount, processed.UnmatchedEndCount);
    }

    [TestMethod]
    public void Analyze_RecycledThreadId_DoesNotCloseTheOtherTracksFrame()
    {
        var session = CreateSession(new[]
        {
            new TraceEvent(5, 7, 10, TraceEventKind.Begin, 0, 0, 1, 1),
            new TraceEvent(5, 7, 1000, TraceEventKind.End, 0, 0, 1, 2)
        });

        var stats = session.Analyze();

        Assert.IsEmpty(stats.ByTotalTimeDesc,
            "an End from another writer must not close a frame opened on a different track");
        Assert.AreEqual(1, stats.UnmatchedBeginCount);
        Assert.AreEqual(1, stats.UnmatchedEndCount);
        Assert.AreEqual(2, stats.ThreadsSeen, "writers are counted per track, not per managed thread id");
    }

    [TestMethod]
    public void Process_RecycledThreadId_KeepsCallTreesApart()
    {
        var session = CreateSession(new[]
        {
            new TraceEvent(1, 7, 10, TraceEventKind.Begin, 0, 0, 1, 1),
            new TraceEvent(1, 7, 20, TraceEventKind.End, 0, 0, 2, 1),
            new TraceEvent(2, 7, 30, TraceEventKind.Begin, 0, 0, 1, 2),
            new TraceEvent(2, 7, 40, TraceEventKind.End, 0, 0, 2, 2)
        });

        var processed = session.Process();

        Assert.HasCount(2, processed.Threads);
        foreach (var thread in processed.Threads)
            Assert.AreEqual(7, thread.ThreadId, "the managed thread id stays available for display");

        CollectionAssert.AreEquivalent(
            new[] { 1, 2 },
            processed.Threads.Select(t => t.Root.Children[0].Id).ToArray());
    }

    [TestMethod]
    public void EventCounts_TotalMatchesSession_ScopeCountsOnlyBeginAndEnd()
    {
        var session = CreateSession(new[]
        {
            new TraceEvent(1, 1, 10, TraceEventKind.Begin, 0, 0),
            new TraceEvent(2, 1, 20, TraceEventKind.Instant, 0, 0),
            new TraceEvent(3, 1, 30, TraceEventKind.FlowStart, 42, 0),
            new TraceEvent(3, 1, 40, TraceEventKind.FlowEnd, 42, 0),
            new TraceEvent(4, 1, 50, TraceEventKind.Counter, 0, 7),
            new TraceEvent(1, 1, 60, TraceEventKind.End, 0, 0)
        });

        var stats = session.Analyze();
        var processed = session.Process();

        Assert.AreEqual(session.EventCount, stats.TotalEventCount);
        Assert.AreEqual(session.EventCount, processed.TotalEventCount);
        Assert.AreEqual(2, stats.ScopeEventCount);
        Assert.AreEqual(2, processed.ScopeEventCount);
    }

    private static TraceSession CreateSession(TraceEvent[] events)
    {
        var capacity = Math.Max(1, events.Length);
        var chunk = new Chunk(capacity);
        Array.Copy(events, chunk.Events, events.Length);
        chunk.Count = events.Length;

        var options = new SessionOptions { ChunkCapacity = capacity };
        var start = events.Length > 0 ? events[0].Timestamp : 0;
        var end = events.Length > 0 ? events[events.Length - 1].Timestamp : start;

        return new TraceSession(new[] { chunk }, start, end, options, new Dictionary<int, string>(), 0, 0, 0, false);
    }
}