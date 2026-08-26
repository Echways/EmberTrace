using System.Diagnostics;

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
}
