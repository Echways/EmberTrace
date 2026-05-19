using System.Linq;
using EmberTrace.Sessions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class EventKindTests
{
    [TestMethod]
    public void Instant_RecordsExactlyOneInstantEvent()
    {
        var ts = Run(s => s.Instant(1));
        var events = Events(ts);

        Assert.AreEqual(1, events.Length);
        Assert.AreEqual(TraceEventKind.Instant, events[0].Kind);
        Assert.AreEqual(1, events[0].Id);
    }

    [TestMethod]
    public void Instant_ValueAndFlowIdAreZero()
    {
        var ts = Run(s => s.Instant(7));
        var e = Events(ts)[0];

        Assert.AreEqual(0L, e.Value);
        Assert.AreEqual(0L, e.FlowId);
    }

    [TestMethod]
    public void Instant_WithoutRunningSession_ProducesNoEvents()
    {
        var ts = new TracingSession();
        ts.Instant(1);
        ts.Start();
        var session = ts.Stop();

        Assert.AreEqual(0L, session.EventCount);
    }

    [TestMethod]
    public void Instant_MultipleIds_AllRecorded()
    {
        var ts = Run(s =>
        {
            s.Instant(10);
            s.Instant(20);
            s.Instant(30);
        });
        var events = Events(ts);

        Assert.AreEqual(3, events.Length);
        CollectionAssert.AreEquivalent(new[] { 10, 20, 30 }, events.Select(e => e.Id).ToArray());
        Assert.IsTrue(events.All(e => e.Kind == TraceEventKind.Instant));
    }

    [DataTestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(100L)]
    [DataRow(-1L)]
    [DataRow(long.MaxValue)]
    [DataRow(long.MinValue)]
    public void Counter_ValueIsPreservedExactly(long value)
    {
        var ts = Run(s => s.Counter(5, value));
        var e = Events(ts)[0];

        Assert.AreEqual(TraceEventKind.Counter, e.Kind);
        Assert.AreEqual(5, e.Id);
        Assert.AreEqual(value, e.Value);
    }

    [TestMethod]
    public void Counter_FlowIdIsAlwaysZero()
    {
        var ts = Run(s => s.Counter(1, 42));

        Assert.AreEqual(0L, Events(ts)[0].FlowId);
    }

    [TestMethod]
    public void Counter_MultipleCallsSameId_EachRecordedSeparately()
    {
        var ts = Run(s =>
        {
            s.Counter(99, 10);
            s.Counter(99, 20);
            s.Counter(99, 30);
        });
        var events = Events(ts).Where(e => e.Id == 99).ToArray();

        Assert.AreEqual(3, events.Length);
        CollectionAssert.AreEqual(new long[] { 10, 20, 30 }, events.Select(e => e.Value).ToArray());
    }

    [TestMethod]
    public void Counter_WithoutRunningSession_ProducesNoEvents()
    {
        var ts = new TracingSession();
        ts.Counter(1, 42);
        ts.Start();
        var session = ts.Stop();

        Assert.AreEqual(0L, session.EventCount);
    }

    [TestMethod]
    public void Scope_EmitsBeginThenEnd()
    {
        var ts = Run(s =>
        {
            using var _ = s.Scope(3);
        });
        var events = Events(ts);

        Assert.AreEqual(2, events.Length);
        Assert.AreEqual(TraceEventKind.Begin, events[0].Kind);
        Assert.AreEqual(TraceEventKind.End,   events[1].Kind);
        Assert.AreEqual(3, events[0].Id);
        Assert.AreEqual(3, events[1].Id);
    }

    [TestMethod]
    public void FlowStart_EmitsFlowStartKindWithFlowId()
    {
        var ts = Run(s =>
        {
            long flowId = s.NewFlowId();
            s.FlowStart(11, flowId);
        });
        var e = Events(ts).Single(x => x.Kind == TraceEventKind.FlowStart);

        Assert.AreEqual(11, e.Id);
        Assert.AreNotEqual(0L, e.FlowId);
    }

    [TestMethod]
    public void FlowStep_EmitsFlowStepKind()
    {
        var ts = Run(s =>
        {
            long flowId = s.NewFlowId();
            s.FlowStart(1, flowId);
            s.FlowStep(1, flowId);
        });
        var step = Events(ts).Single(x => x.Kind == TraceEventKind.FlowStep);

        Assert.AreEqual(1, step.Id);
    }

    [TestMethod]
    public void FlowEnd_EmitsFlowEndKind()
    {
        var ts = Run(s =>
        {
            long flowId = s.NewFlowId();
            s.FlowStart(1, flowId);
            s.FlowEnd(1, flowId);
        });
        var end = Events(ts).Single(x => x.Kind == TraceEventKind.FlowEnd);

        Assert.AreEqual(1, end.Id);
    }

    [TestMethod]
    public void FlowStartNew_ProducesFlowStartWithNonZeroFlowId()
    {
        var ts = Run(s => s.FlowStartNew(7));
        var e = Events(ts).Single();

        Assert.AreEqual(TraceEventKind.FlowStart, e.Kind);
        Assert.AreNotEqual(0L, e.FlowId);
    }

    [TestMethod]
    public void FlowStart_ZeroFlowId_ProducesNoEvent()
    {
        var ts = Run(s => s.FlowStart(1, 0));

        Assert.AreEqual(0L, ts.EventCount, "FlowStart with flowId=0 must be silently ignored");
    }

    [TestMethod]
    public void FlowEvents_ShareSameFlowId()
    {
        var ts = Run(s =>
        {
            long flowId = s.NewFlowId();
            s.FlowStart(1, flowId);
            s.FlowStep(1, flowId);
            s.FlowEnd(1, flowId);
        });
        var events = Events(ts);

        Assert.AreEqual(3, events.Length);
        var firstFlowId = events[0].FlowId;
        Assert.IsTrue(events.All(e => e.FlowId == firstFlowId),
            "All flow events for the same flow should carry the same FlowId");
    }

    [TestMethod]
    public void MixedKinds_AllRecordedWithCorrectKinds()
    {
        var ts = Run(s =>
        {
            s.Instant(1);
            s.Counter(2, 99);
            using var _ = s.Scope(3);
            long flowId = s.NewFlowId();
            s.FlowStart(4, flowId);
            s.FlowEnd(4, flowId);
        });

        var events = Events(ts);

        Assert.IsTrue(events.Any(e => e.Kind == TraceEventKind.Instant));
        Assert.IsTrue(events.Any(e => e.Kind == TraceEventKind.Counter));
        Assert.IsTrue(events.Any(e => e.Kind == TraceEventKind.Begin));
        Assert.IsTrue(events.Any(e => e.Kind == TraceEventKind.End));
        Assert.IsTrue(events.Any(e => e.Kind == TraceEventKind.FlowStart));
        Assert.IsTrue(events.Any(e => e.Kind == TraceEventKind.FlowEnd));
    }

    private static TraceSession Run(System.Action<TracingSession> action)
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions { ChunkCapacity = 256 });
        action(ts);
        return ts.Stop();
    }

    private static TraceEventRecord[] Events(TraceSession session)
    {
        var list = new System.Collections.Generic.List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            list.Add(e);
        return list.ToArray();
    }
}
