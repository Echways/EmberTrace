using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class SnapshotTests
{
    [TestMethod]
    public void Snapshot_KeepsTheSessionRunningAndAccumulating()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 100; i++)
            tracing.Instant(7);

        var snapshot = tracing.Snapshot();

        Assert.IsTrue(tracing.IsRunning);

        for (var i = 0; i < 100; i++)
            tracing.Instant(7);

        var stopped = tracing.Stop();

        Assert.AreEqual(100L, snapshot.EventCount);
        Assert.AreEqual(200L, stopped.EventCount);
        Assert.IsTrue(snapshot.IsSnapshot);
        Assert.IsFalse(stopped.IsSnapshot);
    }

    [TestMethod]
    public void Snapshot_TakenTwice_OverlapsRatherThanDrains()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 10; i++)
            tracing.Instant(7);

        Assert.AreEqual(10L, tracing.Snapshot().EventCount);
        Assert.AreEqual(10L, tracing.Snapshot().EventCount);
    }

    [TestMethod]
    public void Snapshot_IsDetachedFromLaterWrites()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
        tracing.Instant(1);

        var snapshot = tracing.Snapshot();

        for (var i = 0; i < 10; i++)
            tracing.Instant(2);

        CollectionAssert.AreEqual(new[] { 1 }, snapshot.Events().Select(e => e.Id).ToArray());
    }

    [TestMethod]
    public void Snapshot_TimestampsBoundEveryEvent()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 100; i++)
            tracing.Instant(7);

        var snapshot = tracing.Snapshot();

        Assert.IsTrue(snapshot.Events().All(e =>
            e.Timestamp >= snapshot.StartTimestamp && e.Timestamp <= snapshot.EndTimestamp));
    }

    [TestMethod]
    public void Snapshot_WhenNotRunning_ReturnsAnEmptySnapshotSession()
    {
        using var tracing = new TracingSession();

        var snapshot = tracing.Snapshot();

        Assert.AreEqual(0L, snapshot.EventCount);
        Assert.IsTrue(snapshot.IsSnapshot);
    }

    [TestMethod]
    public void Snapshot_WithNegativeWindow_Throws()
    {
        using var tracing = new TracingSession();
        tracing.Start();

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tracing.Snapshot(TimeSpan.FromSeconds(-1)));
        Assert.IsTrue(tracing.IsRunning);
    }

    [TestMethod]
    public void Snapshot_WithWindow_KeepsOnlyRecentEvents()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 50; i++)
            tracing.Instant(7);

        Thread.Sleep(300);

        for (var i = 0; i < 50; i++)
            tracing.Instant(8);

        var snapshot = tracing.Snapshot(TimeSpan.FromMilliseconds(100));

        Assert.AreEqual(50L, snapshot.EventCount);
        Assert.IsTrue(snapshot.Events().All(e => e.Id == 8));
        Assert.IsGreaterThan(tracing.Stop().StartTimestamp, snapshot.StartTimestamp);
    }

    [TestMethod]
    public void Snapshot_UnderConcurrentWritersAndRecycling_StaysConsistent()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions
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
                using (tracing.Scope(4242))
                {
                }

                ready.Signal();

                while (!stop.IsCancellationRequested)
                    using (tracing.Scope(4242))
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
                var snapshot = tracing.Snapshot();

                Assert.IsTrue(tracing.IsRunning);

                foreach (var e in snapshot.EnumerateEvents())
                {
                    Assert.AreEqual(4242, e.Id);
                    Assert.IsTrue(e.Kind is TraceEventKind.Begin or TraceEventKind.End);
                    Assert.IsTrue(e.Timestamp >= snapshot.StartTimestamp && e.Timestamp <= snapshot.EndTimestamp);
                    observed++;
                }
            }
        }
        finally
        {
            stop.Cancel();
            Task.WaitAll(workers);
        }

        Assert.IsGreaterThan(0L, observed);
    }

    [TestMethod]
    public void Start_WithAnInvalidRetentionWindow_ThrowsAndStaysStopped()
    {
        using var tracing = new TracingSession();

        Assert.ThrowsExactly<ArgumentException>(() => tracing.Start(new SessionOptions
        {
            MaxRetentionWindow = TimeSpan.FromSeconds(5),
            OverflowPolicy = OverflowPolicy.DropNew
        }));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tracing.Start(new SessionOptions
        {
            MaxRetentionWindow = TimeSpan.FromDays(2),
            OverflowPolicy = OverflowPolicy.DropOldest
        }));

        Assert.IsFalse(tracing.IsRunning);
    }
}
