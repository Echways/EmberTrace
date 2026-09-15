using System.Diagnostics;
using EmberTrace.Sessions;
using Bridge = EmberTrace.ActivityBridge.ActivityBridge;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public sealed class ActivityBridgeTests
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    [TestMethod]
    public void FlowIdFromTraceId_IsStablePositiveAndDistinctPerTraceId()
    {
        var first = Bridge.FlowIdFromTraceId(TraceId);

        Assert.IsGreaterThan(0L, first);
        Assert.AreEqual(first, Bridge.FlowIdFromTraceId(TraceId));
        Assert.AreNotEqual(first, Bridge.FlowIdFromTraceId("00f067aa0ba902b7a3ce929d0e0e4736"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void FlowIdFromTraceId_ReturnsZeroForBlankInput(string traceId)
    {
        Assert.AreEqual(0L, Bridge.FlowIdFromTraceId(traceId));
    }

    [TestMethod]
    public void FlowIdFromActivityTraceId_MatchesTheHexStringOverloadWithoutAllocating()
    {
        var traceId = ActivityTraceId.CreateFromString(TraceId);
        Bridge.FlowIdFromTraceId(traceId);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var flowId = Bridge.FlowIdFromTraceId(traceId);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(Bridge.FlowIdFromTraceId(TraceId), flowId);
        Assert.AreEqual(0L, allocated);
    }

    [TestMethod]
    [DataRow(ActivityIdFormat.W3C, true)]
    [DataRow(ActivityIdFormat.Hierarchical, false)]
    public void TryGetCurrentFlowId_ReadsOnlyW3CActivities(ActivityIdFormat format, bool expected)
    {
        using var activity = StartActivity(format);

        Assert.AreEqual(expected, Bridge.TryGetCurrentFlowId(out var flowId));
        Assert.AreEqual(expected ? Bridge.FlowIdFromTraceId(activity.TraceId) : 0L, flowId);
    }

    [TestMethod]
    public void FlowFromActivityCurrent_RecordsStartStepAndEndUnderTheTraceFlowId()
    {
        using var activity = StartActivity(ActivityIdFormat.W3C);
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });

        var flowId = tracing.FlowFromActivityCurrent(77);
        var events = tracing.Stop().SortedEvents();

        Assert.AreEqual(Bridge.FlowIdFromTraceId(activity.TraceId), flowId);
        CollectionAssert.AreEqual(
            new[] { TraceEventKind.FlowStart, TraceEventKind.FlowStep, TraceEventKind.FlowEnd },
            events.Select(e => e.Kind).ToArray());
        Assert.IsTrue(events.All(e => e.Id == 77 && e.FlowId == flowId));
    }

    [TestMethod]
    public void FlowFromActivityCurrent_WithoutAnActivity_ReturnsZeroAndRecordsNothing()
    {
        Activity.Current = null;
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });

        Assert.AreEqual(0L, tracing.FlowFromActivityCurrent(77));
        Assert.AreEqual(0L, tracing.Stop().EventCount);
    }

    private static Activity StartActivity(ActivityIdFormat format)
    {
        Activity.Current = null;
        var activity = new Activity("probe");
        activity.SetIdFormat(format);
        return activity.Start();
    }
}
