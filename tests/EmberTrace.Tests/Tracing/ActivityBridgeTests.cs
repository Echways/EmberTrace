using System.Diagnostics;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public sealed class ActivityBridgeTests
{
    [TestMethod]
    public void FlowIdFromTraceId_IsStableAndPositive()
    {
        var first = ActivityBridge.ActivityBridge.FlowIdFromTraceId("4bf92f3577b34da6a3ce929d0e0e4736");
        var second = ActivityBridge.ActivityBridge.FlowIdFromTraceId("4bf92f3577b34da6a3ce929d0e0e4736");

        Assert.AreEqual(first, second);
        Assert.IsTrue(first > 0);
    }

    [TestMethod]
    public void FlowIdFromTraceId_DiffersPerTraceId()
    {
        var first = ActivityBridge.ActivityBridge.FlowIdFromTraceId("4bf92f3577b34da6a3ce929d0e0e4736");
        var second = ActivityBridge.ActivityBridge.FlowIdFromTraceId("00f067aa0ba902b7a3ce929d0e0e4736");

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void FlowIdFromTraceId_ReturnsZeroForEmpty(string traceId)
    {
        Assert.AreEqual(0L, ActivityBridge.ActivityBridge.FlowIdFromTraceId(traceId));
    }

    [TestMethod]
    public void FlowIdFromTraceId_MatchesActivityTraceId()
    {
        using var activity = new Activity("probe");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var flowId = ActivityBridge.ActivityBridge.FlowIdFromTraceId(activity.TraceId.ToHexString());

        Assert.IsTrue(flowId > 0);
    }

    [TestMethod]
    public void FlowIdFromActivityTraceId_MatchesTheHexStringOverload()
    {
        var traceId = ActivityTraceId.CreateRandom();

        Assert.AreEqual(
            ActivityBridge.ActivityBridge.FlowIdFromTraceId(traceId.ToHexString()),
            ActivityBridge.ActivityBridge.FlowIdFromTraceId(traceId));
    }

    [TestMethod]
    public void FlowIdFromActivityTraceId_DoesNotAllocate()
    {
        var traceId = ActivityTraceId.CreateRandom();
        ActivityBridge.ActivityBridge.FlowIdFromTraceId(traceId);

        var before = GC.GetAllocatedBytesForCurrentThread();
        ActivityBridge.ActivityBridge.FlowIdFromTraceId(traceId);

        Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [TestMethod]
    public void TryGetCurrentFlowId_ReadsTheCurrentW3CActivity()
    {
        using var activity = new Activity("probe");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        Assert.IsTrue(ActivityBridge.ActivityBridge.TryGetCurrentFlowId(out var flowId));
        Assert.AreEqual(ActivityBridge.ActivityBridge.FlowIdFromTraceId(activity.TraceId.ToHexString()), flowId);
    }

    [TestMethod]
    public void TryGetCurrentFlowId_IgnoresHierarchicalActivities()
    {
        using var activity = new Activity("probe");
        activity.SetIdFormat(ActivityIdFormat.Hierarchical);
        activity.Start();

        Assert.IsFalse(ActivityBridge.ActivityBridge.TryGetCurrentFlowId(out var flowId));
        Assert.AreEqual(0L, flowId);
    }

    [TestMethod]
    public void FlowFromActivityCurrent_RecordsStartStepAndEndUnderTheTraceFlowId()
    {
        using var activity = new Activity("probe");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });
        var flowId = session.FlowFromActivityCurrent(77);
        var stopped = session.Stop();

        var kinds = new List<TraceEventKind>();
        foreach (var e in stopped.EnumerateEventsSorted())
            if (e.FlowId == flowId)
                kinds.Add(e.Kind);

        Assert.AreEqual(ActivityBridge.ActivityBridge.FlowIdFromTraceId(activity.TraceId), flowId);
        CollectionAssert.AreEqual(
            new[] { TraceEventKind.FlowStart, TraceEventKind.FlowStep, TraceEventKind.FlowEnd }, kinds);
    }
}
