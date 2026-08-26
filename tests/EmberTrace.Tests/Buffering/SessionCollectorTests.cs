using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class SessionCollectorTests
{
    [TestMethod]
    public async Task AddChunk_IsThreadSafe()
    {
        var pool = new ChunkPool(8);
        var options = new SessionOptions
        {
            ChunkCapacity = 8,
            OverflowPolicy = OverflowPolicy.DropNew
        };
        var collector = new SessionCollector(options, pool, options.ChunkCapacity);

        const int chunkTasks = 6;
        const int chunksPerTask = 500;

        var addChunkTasks = Enumerable.Range(0, chunkTasks)
            .Select(_idx => Task.Run(() =>
            {
                for (var i = 0; i < chunksPerTask; i++)
                    collector.TryRentChunk(out _);
            }));

        await Task.WhenAll(addChunkTasks);

        Assert.HasCount(chunkTasks * chunksPerTask, collector.Chunks);
    }

    [TestMethod]
    public void Clear_ResetsCollections()
    {
        var pool = new ChunkPool(4);
        var options = new SessionOptions
        {
            ChunkCapacity = 4,
            OverflowPolicy = OverflowPolicy.DropNew
        };
        var collector = new SessionCollector(options, pool, options.ChunkCapacity);

        collector.TryRentChunk(out _);

        collector.Clear();

        Assert.IsEmpty(collector.Chunks);
        Assert.IsFalse(collector.IsClosed);
    }

    [TestMethod]
    public void Reset_BumpsChunkVersion()
    {
        var chunk = new Chunk(4);
        var before = chunk.Version;

        chunk.Reset();

        Assert.AreNotEqual(before, chunk.Version);
    }

    [TestMethod]
    public void PoolRoundTrip_BumpsChunkVersion()
    {
        var pool = new ChunkPool(4);
        var chunk = pool.Rent();
        var before = chunk.Version;

        pool.Return(chunk);

        Assert.IsTrue(chunk.Version > before);
    }

    [TestMethod]
    public void BeginSnapshot_CapturesCurrentCounts()
    {
        var options = new SessionOptions
        {
            ChunkCapacity = 4,
            OverflowPolicy = OverflowPolicy.DropNew
        };

        var collector = new SessionCollector(options, new ChunkPool(4), 4);
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
    public void BeginSnapshot_KeepsCapturedChunkAliveWhileOpen()
    {
        var options = new SessionOptions
        {
            ChunkCapacity = 2,
            MaxTotalChunks = 2,
            OverflowPolicy = OverflowPolicy.DropOldest
        };

        var collector = new SessionCollector(options, new ChunkPool(2), 2);
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
        Assert.IsTrue(oldest.Chunk.Version > oldest.Version);
    }

    [TestMethod]
    public void Quarantine_StaysBoundedWhileSnapshotIsOpen()
    {
        var options = new SessionOptions
        {
            ChunkCapacity = 2,
            MaxTotalChunks = 2,
            OverflowPolicy = OverflowPolicy.DropOldest
        };

        var collector = new SessionCollector(options, new ChunkPool(2), 2);
        var writer = new ThreadWriter(collector, default, 1);

        for (var i = 0; i < 3; i++)
            writer.Write(7, TraceEventKind.Instant, 0, 0);

        var captures = collector.BeginSnapshot();

        try
        {
            for (var i = 0; i < 500; i++)
                writer.Write(7, TraceEventKind.Instant, 0, 0);

            Assert.AreEqual(captures[0].Version, captures[0].Chunk.Version);
            Assert.IsTrue(collector.Chunks.Count <= 2);
        }
        finally
        {
            collector.EndSnapshot();
        }
    }

    [TestMethod]
    public void Retention_DropsChunksOlderThanTheWindow()
    {
        var now = 1_000_000L;

        var options = new SessionOptions
        {
            ChunkCapacity = 2,
            OverflowPolicy = OverflowPolicy.DropOldest,
            MaxRetentionWindow = TimeSpan.FromSeconds(1)
        };

        var collector = new SessionCollector(options, new ChunkPool(2), 2, () => now);
        var writer = new ThreadWriter(collector, default, 1);

        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);
        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);

        now += Timestamp.Frequency * 5;

        writer.WriteAt(2, TraceEventKind.Instant, 0, 0, now);

        Assert.HasCount(1, collector.Chunks);
        Assert.AreEqual(1L, collector.DroppedChunks);
        Assert.IsFalse(collector.WasOverflow);
    }

    [TestMethod]
    public void Retention_KeepsChunksInsideTheWindow()
    {
        var now = 1_000_000L;

        var options = new SessionOptions
        {
            ChunkCapacity = 2,
            OverflowPolicy = OverflowPolicy.DropOldest,
            MaxRetentionWindow = TimeSpan.FromSeconds(10)
        };

        var collector = new SessionCollector(options, new ChunkPool(2), 2, () => now);
        var writer = new ThreadWriter(collector, default, 1);

        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);
        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);

        now += Timestamp.Frequency;

        writer.WriteAt(2, TraceEventKind.Instant, 0, 0, now);

        Assert.HasCount(2, collector.Chunks);
        Assert.AreEqual(0L, collector.DroppedChunks);
    }

    [TestMethod]
    public void Retention_StopsAfterTheSessionIsClosed()
    {
        var now = 1_000_000L;

        var options = new SessionOptions
        {
            ChunkCapacity = 2,
            OverflowPolicy = OverflowPolicy.DropOldest,
            MaxRetentionWindow = TimeSpan.FromSeconds(1)
        };

        var collector = new SessionCollector(options, new ChunkPool(2), 2, () => now);
        var writer = new ThreadWriter(collector, default, 1);

        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);
        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);
        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, now);

        now += Timestamp.Frequency * 5;
        collector.Close();

        collector.BeginSnapshot();
        collector.EndSnapshot();

        Assert.HasCount(2, collector.Chunks);
        Assert.AreEqual(0L, collector.DroppedChunks);
    }
}
