using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class EventKindTests
{
    [TestMethod]
    public void Instant_RecordsItsIdWithoutValueOrFlow()
    {
        var e = Record(s => s.Instant(7)).Single();

        Assert.AreEqual(TraceEventKind.Instant, e.Kind);
        Assert.AreEqual(7, e.Id);
        Assert.AreEqual(0L, e.Value);
        Assert.AreEqual(0L, e.FlowId);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(1L)]
    [DataRow(-1L)]
    [DataRow(long.MaxValue)]
    [DataRow(long.MinValue)]
    public void Counter_PreservesTheValueExactly(long value)
    {
        var e = Record(s => s.Counter(5, value)).Single();

        Assert.AreEqual(TraceEventKind.Counter, e.Kind);
        Assert.AreEqual(5, e.Id);
        Assert.AreEqual(value, e.Value);
        Assert.AreEqual(0L, e.FlowId);
    }

    [TestMethod]
    public void Scope_EmitsBeginThenEndWithoutAsyncIdentity()
    {
        var events = Record(s =>
        {
            using var _ = s.Scope(3);
        });

        CollectionAssert.AreEqual(
            new[] { (3, TraceEventKind.Begin), (3, TraceEventKind.End) },
            events.Select(e => (e.Id, e.Kind)).ToArray());
        Assert.IsTrue(events.All(e => e.AsyncScopeId == 0 && e.AsyncContextId == 0));
    }

    [TestMethod]
    public void FlowApis_EmitTheirKindsUnderTheGivenFlowId()
    {
        long flowId = 0;
        var events = Record(s =>
        {
            flowId = s.NewFlowId();
            s.FlowStart(11, flowId);
            s.FlowStep(11, flowId);
            s.FlowEnd(11, flowId);
        });

        Assert.AreNotEqual(0L, flowId);
        CollectionAssert.AreEqual(
            new[] { TraceEventKind.FlowStart, TraceEventKind.FlowStep, TraceEventKind.FlowEnd },
            events.Select(e => e.Kind).ToArray());
        Assert.IsTrue(events.All(e => e.Id == 11 && e.FlowId == flowId && e.Value == 0));
    }

    [TestMethod]
    public void FlowStartNew_ReturnsTheFlowIdItRecorded()
    {
        long flowId = 0;
        var e = Record(s => flowId = s.FlowStartNew(7)).Single();

        Assert.AreEqual(TraceEventKind.FlowStart, e.Kind);
        Assert.AreNotEqual(0L, flowId);
        Assert.AreEqual(flowId, e.FlowId);
    }

    [TestMethod]
    [DataRow(TraceEventKind.FlowStart)]
    [DataRow(TraceEventKind.FlowStep)]
    [DataRow(TraceEventKind.FlowEnd)]
    public void FlowApis_WithZeroFlowId_RecordNothing(TraceEventKind kind)
    {
        var events = Record(s =>
        {
            switch (kind)
            {
                case TraceEventKind.FlowStart:
                    s.FlowStart(1, 0);
                    break;
                case TraceEventKind.FlowStep:
                    s.FlowStep(1, 0);
                    break;
                default:
                    s.FlowEnd(1, 0);
                    break;
            }
        });

        Assert.IsEmpty(events);
    }

    [TestMethod]
    public void MixedKinds_AreRecordedInCallOrder()
    {
        var events = Record(s =>
        {
            s.Instant(1);
            s.Counter(2, 99);
            using (s.Scope(3))
            {
                var flowId = s.NewFlowId();
                s.FlowStart(4, flowId);
                s.FlowEnd(4, flowId);
            }
        });

        CollectionAssert.AreEqual(
            new[]
            {
                (1, TraceEventKind.Instant),
                (2, TraceEventKind.Counter),
                (3, TraceEventKind.Begin),
                (4, TraceEventKind.FlowStart),
                (4, TraceEventKind.FlowEnd),
                (3, TraceEventKind.End)
            },
            events.Select(e => (e.Id, e.Kind)).ToArray());
    }

    [TestMethod]
    public async Task EveryApi_BeforeStart_RecordsNothingAndHandsOutInertHandles()
    {
        using var tracing = new TracingSession();

        tracing.Instant(1);
        tracing.Counter(1, 42);
        tracing.FlowStart(1, 5);
        using (tracing.Scope(1))
        {
        }

        await using (tracing.ScopeAsync(1))
        {
        }

        var flow = tracing.Flow(1);
        var handle = tracing.FlowStartNewHandle(1);
        flow.Step();
        flow.Dispose();
        handle.Step();

        Assert.IsFalse(flow.IsValid);
        Assert.IsFalse(handle.IsValid);
        Assert.IsFalse(handle.TryEnd());

        tracing.Start();
        Assert.AreEqual(0L, tracing.Stop().EventCount);
    }

    private static List<TraceEventRecord> Record(Action<TracingSession> body)
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 256 });
        body(tracing);
        return tracing.Stop().SortedEvents();
    }
}
