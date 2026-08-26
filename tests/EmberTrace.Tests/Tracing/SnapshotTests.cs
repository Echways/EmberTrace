using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class SnapshotTests
{
    [TestMethod]
    public void Snapshot_KeepsTheSessionRunningAndAccumulating()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 100; i++)
            session.Instant(7);

        var snapshot = session.Snapshot();

        Assert.IsTrue(session.IsRunning);

        for (var i = 0; i < 100; i++)
            session.Instant(7);

        var stopped = session.Stop();

        Assert.AreEqual(100L, snapshot.EventCount);
        Assert.AreEqual(200L, stopped.EventCount);
        Assert.IsTrue(snapshot.IsSnapshot);
        Assert.IsFalse(stopped.IsSnapshot);
    }

    [TestMethod]
    public void Snapshot_TakenTwice_OverlapsRatherThanDrains()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 10; i++)
            session.Instant(7);

        var first = session.Snapshot();
        var second = session.Snapshot();

        Assert.AreEqual(10L, first.EventCount);
        Assert.AreEqual(10L, second.EventCount);
    }

    [TestMethod]
    public void Snapshot_EndTimestampBoundsEveryEvent()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 100; i++)
            session.Instant(7);

        var snapshot = session.Snapshot();

        foreach (var e in snapshot.EnumerateEvents())
        {
            Assert.IsTrue(e.Timestamp >= snapshot.StartTimestamp);
            Assert.IsTrue(e.Timestamp <= snapshot.EndTimestamp);
        }
    }

    [TestMethod]
    public void Snapshot_WhenNotRunning_ReturnsAnEmptySnapshotSession()
    {
        using var session = new TracingSession();

        var snapshot = session.Snapshot();

        Assert.AreEqual(0L, snapshot.EventCount);
        Assert.IsTrue(snapshot.IsSnapshot);
    }

    [TestMethod]
    public void Snapshot_WithNegativeWindow_Throws()
    {
        using var session = new TracingSession();
        session.Start();

        try
        {
            session.Snapshot(TimeSpan.FromSeconds(-1));
            Assert.Fail("Expected ArgumentOutOfRangeException.");
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        finally
        {
            session.Stop();
        }
    }

    [TestMethod]
    public void Snapshot_WithWindow_KeepsOnlyRecentEvents()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 50; i++)
            session.Instant(7);

        Thread.Sleep(300);

        for (var i = 0; i < 50; i++)
            session.Instant(8);

        var snapshot = session.Snapshot(TimeSpan.FromMilliseconds(100));
        session.Stop();

        foreach (var e in snapshot.EnumerateEvents())
            Assert.AreEqual(8, e.Id);

        Assert.AreEqual(50L, snapshot.EventCount);
    }

    [TestMethod]
    public void Snapshot_UnderConcurrentWritersAndRecycling_StaysConsistent()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions
        {
            ChunkCapacity = 512,
            MaxTotalChunks = 8,
            OverflowPolicy = OverflowPolicy.DropOldest
        });

        using var stop = new CancellationTokenSource();
        using var ready = new CountdownEvent(4);

        var workers = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() =>
            {
                using (session.Scope(4242))
                {
                }

                ready.Signal();

                while (!stop.IsCancellationRequested)
                    using (session.Scope(4242))
                    {
                    }
            }))
            .ToArray();

        var observed = 0L;

        try
        {
            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(30)));

            for (var i = 0; i < 100; i++)
            {
                var snapshot = session.Snapshot();

                Assert.IsTrue(session.IsRunning);
                Assert.IsTrue(snapshot.IsSnapshot);

                foreach (var e in snapshot.EnumerateEvents())
                {
                    Assert.AreEqual(4242, e.Id);
                    Assert.IsTrue(e.Kind is TraceEventKind.Begin or TraceEventKind.End);
                    Assert.IsTrue(e.Timestamp >= snapshot.StartTimestamp);
                    Assert.IsTrue(e.Timestamp <= snapshot.EndTimestamp);
                    observed++;
                }
            }
        }
        finally
        {
            stop.Cancel();
            Task.WaitAll(workers);
            session.Stop();
        }

        Assert.IsTrue(observed > 0);
    }

    [TestMethod]
    public void Start_WithRetentionWindowAndWrongPolicy_Throws()
    {
        using var session = new TracingSession();

        try
        {
            session.Start(new SessionOptions
            {
                MaxRetentionWindow = TimeSpan.FromSeconds(5),
                OverflowPolicy = OverflowPolicy.DropNew
            });

            Assert.Fail("Expected ArgumentException.");
        }
        catch (ArgumentException)
        {
        }

        Assert.IsFalse(session.IsRunning);
    }
}
