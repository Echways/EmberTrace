using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class TracingSessionLifecycleTests
{
    [TestMethod]
    public void Stop_ReturnsTheRecordedSessionAndRemembersIt()
    {
        var tracing = new TracingSession();
        tracing.Start();
        tracing.Instant(1);

        var stopped = tracing.Stop();

        Assert.IsFalse(tracing.IsRunning);
        Assert.AreEqual(1L, stopped.EventCount);
        Assert.AreSame(stopped, tracing.LastSession);
    }

    [TestMethod]
    public void Dispose_WhenRunning_StopsAndKeepsTheCollectedEvents()
    {
        var tracing = new TracingSession();
        tracing.Start();
        tracing.Instant(42);

        tracing.Dispose();

        Assert.IsFalse(tracing.IsRunning);
        Assert.AreEqual(1L, tracing.LastSession!.EventCount);
    }

    [TestMethod]
    public void Dispose_WhenNotRunning_OrTwice_IsANoOp()
    {
        var tracing = new TracingSession();

        tracing.Dispose();
        tracing.Dispose();

        Assert.IsFalse(tracing.IsRunning);
        Assert.IsNull(tracing.LastSession);
    }

    [TestMethod]
    public void Start_AfterDispose_Throws()
    {
        var tracing = new TracingSession();
        tracing.Start();
        tracing.Dispose();

        Assert.ThrowsExactly<ObjectDisposedException>(() => tracing.Start());
    }

    [TestMethod]
    public void StartTwice_Throws_AndStopWithoutStart_Throws()
    {
        using var tracing = new TracingSession();
        tracing.Start();

        Assert.ThrowsExactly<InvalidOperationException>(() => tracing.Start());

        tracing.Stop();

        Assert.ThrowsExactly<InvalidOperationException>(() => tracing.Stop());
    }

    [TestMethod]
    public void Restart_BeginsWithAnEmptySession()
    {
        using var tracing = new TracingSession();
        tracing.Start();
        tracing.Instant(1);
        tracing.Stop();

        tracing.Start();
        tracing.Instant(2);

        CollectionAssert.AreEqual(new[] { 2 }, tracing.Stop().Events().Select(e => e.Id).ToArray());
    }

    [TestMethod]
    public void OnStopped_ReceivesEverySessionFromStopAndDispose()
    {
        var received = new List<TraceSession>();

        using (var tracing = new TracingSession(received.Add))
        {
            tracing.Start();
            tracing.Instant(1);
            var explicitStop = tracing.Stop();

            tracing.Start();
            tracing.Instant(7);
            tracing.Instant(7);

            Assert.HasCount(1, received);
            Assert.AreSame(explicitStop, received[0]);
        }

        Assert.HasCount(2, received);
        Assert.AreEqual(2L, received[1].EventCount);
    }

    [TestMethod]
    public void Constructor_WithNullCallback_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new TracingSession(null!));
    }

    [TestMethod]
    public void Stop_DoesNotCopyFullChunks()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 16_384 });

        for (var i = 0; i < 200_000; i++)
            tracing.Instant(1);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopped = tracing.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(200_000L, stopped.EventCount);
        Assert.IsLessThan(1_000_000, allocated);
    }

    [TestMethod]
    public void Stop_WhileOtherThreadsWrite_ReturnsAConsistentSession()
    {
        const int id = 7301;

        for (var round = 0; round < 20; round++)
        {
            using var tracing = new TracingSession();
            tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
            using var stop = new ManualResetEventSlim();

            var writers = Enumerable.Range(0, 4).Select(_ => new Thread(() =>
            {
                while (!stop.IsSet)
                    using (tracing.Scope(id))
                    {
                    }
            })).ToArray();

            foreach (var writer in writers)
                writer.Start();

            Thread.Sleep(5);
            var session = tracing.Stop();
            var count = session.EventCount;
            var first = session.SortedEvents();

            stop.Set();
            foreach (var writer in writers)
                writer.Join();

            Assert.AreEqual(count, session.EventCount);
            CollectionAssert.AreEqual(first, session.SortedEvents());
            Assert.IsTrue(first.All(e => e.Id == id && e.Timestamp > 0 && e.Kind is TraceEventKind.Begin or TraceEventKind.End));

            foreach (var track in first.GroupBy(e => e.TrackId))
            {
                var sequences = track.Select(e => e.Sequence).ToArray();
                CollectionAssert.AreEqual(
                    Enumerable.Range(0, sequences.Length).Select(i => sequences[0] + i).ToArray(), sequences);
            }
        }
    }
}
