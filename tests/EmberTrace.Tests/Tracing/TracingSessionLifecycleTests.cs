using System;
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
    public void Dispose_WhenRunning_KeepsCollectedEventsInLastSession()
    {
        var ts = new TracingSession();
        ts.Start();
        ts.Instant(42);
        ts.Dispose();

        Assert.IsNotNull(ts.LastSession);
        Assert.AreEqual(1, ts.LastSession!.EventCount);
    }

    [TestMethod]
    public void Dispose_WithCallback_HandsOverCollectedSession()
    {
        TraceSessionCapture captured = new();

        using (var ts = new TracingSession(captured.Accept))
        {
            ts.Start();
            ts.Instant(7);
        }

        Assert.AreEqual(1, captured.Count);
        Assert.AreEqual(1, captured.Session!.EventCount);
    }

    [TestMethod]
    public void Stop_RecordsLastSession()
    {
        var ts = new TracingSession();
        ts.Start();
        ts.Instant(1);
        var stopped = ts.Stop();

        Assert.AreSame(stopped, ts.LastSession);
    }

    [TestMethod]
    public void Start_AfterDispose_Throws()
    {
        using var ts = new TracingSession();
        ts.Start();
        ts.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => ts.Start());
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var ts = new TracingSession();
        ts.Start();
        ts.Dispose();
        ts.Dispose();

        Assert.IsFalse(ts.IsRunning);
    }

    [TestMethod]
    public void UsingBlock_ExceptionBetweenStartAndStop_PreservesCollectedEvents()
    {
        var ts = new TracingSession();

        try
        {
            ts.Start();
            using (ts)
            {
                ts.Instant(3);
                throw new InvalidOperationException("simulated");
            }
        }
        catch (InvalidOperationException) { }

        Assert.IsFalse(ts.IsRunning);
        Assert.IsNotNull(ts.LastSession);
        Assert.AreEqual(1, ts.LastSession!.EventCount);
    }

    private sealed class TraceSessionCapture
    {
        public int Count { get; private set; }
        public EmberTrace.Sessions.TraceSession? Session { get; private set; }

        public void Accept(EmberTrace.Sessions.TraceSession session)
        {
            Count++;
            Session = session;
        }
    }
}
