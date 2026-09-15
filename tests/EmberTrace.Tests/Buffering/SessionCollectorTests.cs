using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class SessionCollectorTests
{
    [TestMethod]
    public async Task TryRentChunk_ConcurrentRenters_RegistersEveryChunk()
    {
        const int renters = 6;
        const int chunksPerRenter = 500;

        var collector = Collectors.Create(capacity: 8);

        await Task.WhenAll(Enumerable.Range(0, renters).Select(renter => Task.Run(() =>
        {
            for (var i = 0; i < chunksPerRenter; i++)
                collector.TryRentChunk(out _);
        })));

        Assert.HasCount(renters * chunksPerRenter, collector.Chunks);
        Assert.HasCount(renters * chunksPerRenter, collector.Chunks.Distinct());
    }

    [TestMethod]
    public void NoEventLimit_AcceptsEveryEvent()
    {
        var collector = Collectors.Create();

        for (var i = 0; i < 1024; i++)
            Assert.IsTrue(collector.TryAcceptEvent());

        Assert.AreEqual(0L, collector.DroppedEvents);
        Assert.IsFalse(collector.WasOverflow);
    }

    [TestMethod]
    public void Close_RejectsEventsAndChunks()
    {
        var collector = Collectors.Create();

        collector.Close();

        Assert.IsTrue(collector.IsClosed);
        Assert.IsFalse(collector.TryAcceptEvent());
        Assert.IsFalse(collector.TryRentChunk(out var chunk));
        Assert.IsNull(chunk);
    }

    [TestMethod]
    public void Clear_ResetsChunksCountersAndClosedState()
    {
        var collector = Collectors.Create(maxEvents: 1);
        collector.TryRentChunk(out _);
        collector.TryAcceptEvent();
        collector.TryAcceptEvent();
        collector.RecordSampledOutEvent();
        collector.RegisterThreadName(1, "worker");
        collector.Close();

        collector.Clear();

        Assert.IsEmpty(collector.Chunks);
        Assert.IsEmpty(collector.ThreadNames);
        Assert.IsFalse(collector.IsClosed);
        Assert.IsFalse(collector.WasOverflow);
        Assert.AreEqual(0L, collector.DroppedEvents);
        Assert.AreEqual(0L, collector.SampledOutEvents);
        Assert.IsTrue(collector.TryAcceptEvent());
    }

    [TestMethod]
    public void RecordSampledOutEvent_AccumulatesWithoutMarkingOverflow()
    {
        var collector = Collectors.Create();

        for (var i = 0; i < 3; i++)
            collector.RecordSampledOutEvent();

        Assert.AreEqual(3L, collector.SampledOutEvents);
        Assert.IsFalse(collector.WasOverflow);
    }

    [TestMethod]
    [DataRow("worker", true)]
    [DataRow("   ", false)]
    [DataRow("", false)]
    public void RegisterThreadName_IgnoresBlankNames(string name, bool registered)
    {
        var collector = Collectors.Create();

        collector.RegisterThreadName(9, name);

        Assert.AreEqual(registered, collector.ThreadNames.ContainsKey(9));
    }

    [TestMethod]
    public void BeginSnapshot_CapturesCurrentCounts()
    {
        var collector = Collectors.Create(capacity: 4);
        var writer = new ThreadWriter(collector, default, 1);

        writer.Write(7, TraceEventKind.Instant, 0, 0);
        writer.Write(7, TraceEventKind.Instant, 0, 0);

        var captures = collector.BeginSnapshot();

        try
        {
            writer.Write(7, TraceEventKind.Instant, 0, 0);

            Assert.HasCount(1, captures);
            Assert.AreEqual(2, captures[0].Count);
            Assert.AreEqual(3, captures[0].Chunk.Count);
        }
        finally
        {
            collector.EndSnapshot();
        }
    }

    [TestMethod]
    public void BeginSnapshot_KeepsCapturedChunkAliveUntilEndSnapshot()
    {
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxChunks: 2, capacity: 2);
        var writer = new ThreadWriter(collector, default, 1);

        for (var i = 0; i < 3; i++)
            writer.Write(7, TraceEventKind.Instant, 0, 0);

        var captures = collector.BeginSnapshot();
        var oldest = captures[0];

        for (var i = 0; i < 20; i++)
            writer.Write(7, TraceEventKind.Instant, 0, 0);

        Assert.AreEqual(oldest.Version, oldest.Chunk.Version);
        Assert.AreEqual(oldest.Count, oldest.Chunk.Count);
        Assert.AreEqual(0L, collector.SnapshotDiscardedChunks);

        collector.EndSnapshot();

        Assert.AreEqual(0, oldest.Chunk.Count);
        Assert.IsGreaterThan(oldest.Version, oldest.Chunk.Version);
    }

    [TestMethod]
    public void Quarantine_StaysBoundedWhileASnapshotIsOpen()
    {
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxChunks: 2, capacity: 2);
        var writer = new ThreadWriter(collector, default, 1);

        for (var i = 0; i < 3; i++)
            writer.Write(7, TraceEventKind.Instant, 0, 0);

        var captures = collector.BeginSnapshot();

        try
        {
            for (var i = 0; i < 500; i++)
                writer.Write(7, TraceEventKind.Instant, 0, 0);

            Assert.AreEqual(captures[0].Version, captures[0].Chunk.Version);
            Assert.IsLessThanOrEqualTo(2, collector.Chunks.Count);
        }
        finally
        {
            collector.EndSnapshot();
        }
    }

    [TestMethod]
    [DataRow(1, 5, 1, 1)]
    [DataRow(10, 1, 2, 0)]
    public void Retention_DropsOnlyChunksOlderThanTheWindow(
        int windowSeconds, int elapsedSeconds, int expectedChunks, int expectedDroppedChunks)
    {
        var now = 1_000_000L;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, capacity: 2,
            retention: TimeSpan.FromSeconds(windowSeconds), clock: () => now);
        var writer = new ThreadWriter(collector, default, 1);

        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);
        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);

        now += Timestamp.Frequency * elapsedSeconds;

        writer.WriteAt(2, TraceEventKind.Instant, 0, 0, now);

        Assert.HasCount(expectedChunks, collector.Chunks);
        Assert.AreEqual(expectedDroppedChunks, collector.DroppedChunks);
        Assert.AreEqual(expectedDroppedChunks * 2L, collector.DroppedEvents);
        Assert.IsFalse(collector.WasOverflow);
    }

    [TestMethod]
    public void Retention_StopsAfterTheSessionIsClosed()
    {
        var now = 1_000_000L;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, capacity: 2,
            retention: TimeSpan.FromSeconds(1), clock: () => now);
        var writer = new ThreadWriter(collector, default, 1);

        for (var i = 0; i < 3; i++)
            writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);

        now += Timestamp.Frequency * 5;
        collector.Close();

        collector.BeginSnapshot();
        collector.EndSnapshot();

        Assert.HasCount(2, collector.Chunks);
        Assert.AreEqual(0L, collector.DroppedChunks);
    }
}
