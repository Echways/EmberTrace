using EmberTrace.Sessions;

namespace EmberTrace.Tests.Analysis;

[TestClass]
public class TraceAnalysisTests
{
    [TestMethod]
    public void Analyze_AggregatesDurationsPerIdSortedByTotalTime()
    {
        var session = new TraceScript()
            .Span(1, 0, 1_000)
            .Span(1, 2_000, 5_000)
            .Span(2, 6_000, 11_000)
            .Span(3, 11_000, 11_500, 2)
            .ToSession(end: 20_000);

        var stats = session.Analyze();

        CollectionAssert.AreEqual(new[] { 2, 1, 3 }, stats.ByTotalTimeDesc.Select(r => r.Id).ToArray());

        var first = stats.ByTotalTimeDesc[1];
        Assert.AreEqual(2L, first.Count);
        Assert.AreEqual(4.0, first.TotalMs, 1e-9);
        Assert.AreEqual(2.0, first.AverageMs, 1e-9);
        Assert.AreEqual(1.0, first.MinMs, 1e-9);
        Assert.AreEqual(3.0, first.MaxMs, 1e-9);

        Assert.AreEqual(20.0, stats.DurationMs, 1e-9);
        Assert.AreEqual(2, stats.ThreadsSeen);
        Assert.AreEqual(8L, stats.TotalEventCount);
        Assert.AreEqual(8L, stats.ScopeEventCount);
    }

    [TestMethod]
    public void Analyze_CountsScopeEventsSeparatelyFromEveryEvent()
    {
        var session = new TraceScript()
            .Begin(1, 10)
            .Instant(2, 20)
            .Flow(TraceEventKind.FlowStart, 3, 30, 42)
            .Flow(TraceEventKind.FlowEnd, 3, 40, 42)
            .Counter(4, 50, 7)
            .End(1, 60)
            .ToSession();

        var stats = session.Analyze();
        var processed = session.Process();

        Assert.AreEqual(6L, stats.TotalEventCount);
        Assert.AreEqual(6L, processed.TotalEventCount);
        Assert.AreEqual(2L, stats.ScopeEventCount);
        Assert.AreEqual(2L, processed.ScopeEventCount);
    }

    [TestMethod]
    [DataRow(false, 1, 1, 2)]
    [DataRow(true, 2, 1, 0)]
    public void MismatchedEnds_AreRecoveredOrSkippedDependingOnStrictMode(
        bool strict, int mismatched, int unmatchedBegins, int unmatchedEnds)
    {
        var session = new TraceScript()
            .Begin(1, 10)
            .Begin(2, 20)
            .End(1, 30)
            .End(2, 40)
            .End(3, 50)
            .ToSession();

        var stats = session.Analyze(strict);
        var processed = session.Process(strict);

        Assert.AreEqual(mismatched, stats.MismatchedEndCount);
        Assert.AreEqual(unmatchedBegins, stats.UnmatchedBeginCount);
        Assert.AreEqual(unmatchedEnds, stats.UnmatchedEndCount);
        Assert.AreEqual(mismatched, processed.MismatchedEndCount);
        Assert.AreEqual(unmatchedBegins, processed.UnmatchedBeginCount);
        Assert.AreEqual(unmatchedEnds, processed.UnmatchedEndCount);
    }

    [TestMethod]
    public void MismatchedEnd_IsReportedToTheSessionCallback()
    {
        var reported = new List<MismatchedEndInfo>();
        var session = TraceSession.FromEvents(
            new TraceScript().Begin(1, 10).Begin(2, 20).End(1, 30).ToSession().SortedEvents(),
            0, 30, 1_000_000, options: new SessionOptions { OnMismatchedEnd = reported.Add });

        session.Analyze();

        Assert.AreEqual(new MismatchedEndInfo(1, 2, 1, 30), reported.Single());
    }

    [TestMethod]
    public void RecycledThreadId_DoesNotCloseAFrameOpenedOnAnotherTrack()
    {
        var session = TraceSession.FromEvents(
        [
            new(5, 7, 10, TraceEventKind.Begin, 0, 0, 1, 1),
            new(5, 7, 1000, TraceEventKind.End, 0, 0, 1, 2)
        ], 10, 1000, 1_000_000);

        var stats = session.Analyze();

        Assert.IsEmpty(stats.ByTotalTimeDesc);
        Assert.AreEqual(1, stats.UnmatchedBeginCount);
        Assert.AreEqual(1, stats.UnmatchedEndCount);
        Assert.AreEqual(2, stats.ThreadsSeen);
    }

    [TestMethod]
    public void AnalyzeFlows_MeasuresTheWholeFlowAndEachStep()
    {
        var session = new TraceScript()
            .Flow(TraceEventKind.FlowStart, 1, 100, 42)
            .Flow(TraceEventKind.FlowStep, 2, 1_100, 42, 2)
            .Flow(TraceEventKind.FlowEnd, 3, 3_100, 42)
            .ToSession();

        var flow = session.AnalyzeFlows().Single();

        Assert.AreEqual(42L, flow.FlowId);
        Assert.AreEqual(1, flow.Id);
        Assert.AreEqual(100L, flow.StartTimestamp);
        Assert.AreEqual(3_100L, flow.EndTimestamp);
        Assert.AreEqual(3.0, flow.TotalDurationMs, 1e-9);
        CollectionAssert.AreEqual(
            new[] { (1, TraceEventKind.FlowStart, 1.0), (2, TraceEventKind.FlowStep, 2.0) },
            flow.Steps.Select(s => (s.Id, s.Kind, s.DurationMs)).ToArray());
    }

    [TestMethod]
    public void AnalyzeFlows_SkipsIncompleteFlowsAndKeepsTheLongestUpToTop()
    {
        var session = new TraceScript()
            .Flow(TraceEventKind.FlowStart, 1, 0, 1)
            .Flow(TraceEventKind.FlowEnd, 1, 1_000, 1)
            .Flow(TraceEventKind.FlowStart, 1, 0, 2)
            .Flow(TraceEventKind.FlowEnd, 1, 5_000, 2)
            .Flow(TraceEventKind.FlowStart, 1, 0, 3)
            .Flow(TraceEventKind.FlowEnd, 1, 3_000, 3)
            .Flow(TraceEventKind.FlowStart, 1, 0, 4)
            .Flow(TraceEventKind.FlowStep, 1, 9_000, 4)
            .Flow(TraceEventKind.FlowStep, 1, 9_500, 5)
            .Flow(TraceEventKind.FlowEnd, 1, 9_900, 5)
            .ToSession();

        CollectionAssert.AreEqual(new[] { 2L, 3L, 1L }, session.AnalyzeFlows().Select(f => f.FlowId).ToArray());
        CollectionAssert.AreEqual(new[] { 2L, 3L }, session.AnalyzeFlows(top: 2).Select(f => f.FlowId).ToArray());
    }

    [TestMethod]
    public void Extensions_RejectANullSession()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceSessionExtensions.Analyze(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceSessionExtensions.Process(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceSessionExtensions.AnalyzeFlows(null!));
    }
}
