using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class TracingSessionLifecycleTests
{
    [TestMethod]
    public void Dispose_WhenRunning_StopsSession()
    {
        var ts = new TracingSession();
        ts.Start();
        ts.Dispose();

        Assert.IsFalse(ts.IsRunning);
    }

    [TestMethod]
    public void Dispose_WhenNotRunning_DoesNotThrow()
    {
        var ts = new TracingSession();
        ts.Dispose();

        Assert.IsFalse(ts.IsRunning);
    }

    [TestMethod]
    public void Start_AfterDisposeWhileRunning_Succeeds()
    {
        var ts = new TracingSession();
        ts.Start();
        ts.Dispose();

        ts.Start();
        Assert.IsTrue(ts.IsRunning);
        ts.Stop();
    }

    [TestMethod]
    public void UsingBlock_ExceptionBetweenStartAndStop_AllowsNewStart()
    {
        var ts = new TracingSession();

        try
        {
            ts.Start();
            using (ts)
                throw new System.InvalidOperationException("simulated");
        }
        catch (System.InvalidOperationException) { }

        ts.Start();
        Assert.IsTrue(ts.IsRunning);
        ts.Stop();
    }
}
