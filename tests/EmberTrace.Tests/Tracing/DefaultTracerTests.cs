using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
[DoNotParallelize]
public class DefaultTracerTests
{
    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();
    }

    [TestMethod]
    public void Snapshot_ReturnsRecordedEventsAndKeepsRecording()
    {
        var id = Tracer.Id("Snapshot.Default");

        Tracer.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 25; i++)
            Tracer.Instant(id);

        var snapshot = Tracer.Snapshot();
        Tracer.Instant(id);

        Assert.IsTrue(Tracer.IsRunning);
        Assert.IsTrue(snapshot.IsSnapshot);
        Assert.AreEqual(25L, snapshot.EventCount);
        Assert.AreEqual(26L, Tracer.Stop().EventCount);
    }

    [TestMethod]
    public void StartTwice_Throws_AndStopWithoutStart_Throws()
    {
        Tracer.Start();

        Assert.ThrowsExactly<InvalidOperationException>(() => Tracer.Start());

        Tracer.Stop();

        Assert.ThrowsExactly<InvalidOperationException>(() => Tracer.Stop());
    }

    [TestMethod]
    public void ScopesInterleavedWithATracingSessionOnOneThread_EndInTheirOwnProfiler()
    {
        const int tracerId = 11;
        const int sessionId = 22;

        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 256 });
        Tracer.Start(new SessionOptions { ChunkCapacity = 256 });

        var outer = Tracer.Scope(tracerId);
        var inner = tracing.Scope(sessionId);
        inner.Dispose();
        outer.Dispose();

        CollectionAssert.AreEqual(
            new[] { (tracerId, TraceEventKind.Begin), (tracerId, TraceEventKind.End) },
            Tracer.Stop().SortedEvents().Select(e => (e.Id, e.Kind)).ToArray());
        CollectionAssert.AreEqual(
            new[] { (sessionId, TraceEventKind.Begin), (sessionId, TraceEventKind.End) },
            tracing.Stop().SortedEvents().Select(e => (e.Id, e.Kind)).ToArray());
    }
}
