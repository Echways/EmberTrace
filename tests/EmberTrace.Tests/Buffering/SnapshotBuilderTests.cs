using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class SnapshotBuilderTests
{
    [TestMethod]
    public void Copy_DetachesEventsFromTheLiveChunk()
    {
        var chunk = new Chunk(4);
        chunk.TryWrite(new TraceEvent(1, 10, 100, TraceEventKind.Instant, 0, 0, 1, 10));
        chunk.TryWrite(new TraceEvent(2, 10, 200, TraceEventKind.Instant, 0, 0, 2, 10));

        var captures = new[] { new ChunkCapture(chunk, chunk.Version, chunk.Count) };
        var copied = SnapshotBuilder.Copy(captures, 0, out var discarded);

        chunk.Reset();
        chunk.TryWrite(new TraceEvent(99, 10, 300, TraceEventKind.Instant, 0, 0, 3, 10));

        Assert.AreEqual(0, discarded);
        Assert.HasCount(1, copied);
        Assert.AreEqual(2, copied[0].Count);
        Assert.AreEqual(1, copied[0].Events[0].Id);
        Assert.AreEqual(2, copied[0].Events[1].Id);
    }

    [TestMethod]
    public void Copy_SkipsEmptyChunks()
    {
        var empty = new Chunk(4);
        var captures = new[] { new ChunkCapture(empty, empty.Version, 0) };

        var copied = SnapshotBuilder.Copy(captures, 0, out _);

        Assert.IsEmpty(copied);
    }

    [TestMethod]
    public void Copy_HonoursTheCapturedCountNotTheLiveCount()
    {
        var chunk = new Chunk(8);
        for (var i = 0; i < 5; i++)
            chunk.TryWrite(new TraceEvent(i, 10, 100 + i, TraceEventKind.Instant, 0, 0, i, 10));

        var captures = new[] { new ChunkCapture(chunk, chunk.Version, 3) };

        var copied = SnapshotBuilder.Copy(captures, 0, out _);

        Assert.AreEqual(3, copied[0].Count);
    }

    [TestMethod]
    public void Copy_WithWindow_KeepsOnlyEventsAtOrAfterTheCutoff()
    {
        var chunk = new Chunk(8);
        chunk.TryWrite(new TraceEvent(1, 10, 100, TraceEventKind.Instant, 0, 0, 1, 10));
        chunk.TryWrite(new TraceEvent(2, 10, 200, TraceEventKind.Instant, 0, 0, 2, 10));
        chunk.TryWrite(new TraceEvent(3, 10, 300, TraceEventKind.Instant, 0, 0, 3, 10));

        var captures = new[] { new ChunkCapture(chunk, chunk.Version, chunk.Count) };
        var copied = SnapshotBuilder.Copy(captures, 200, out _);

        Assert.HasCount(1, copied);
        Assert.AreEqual(2, copied[0].Count);
        Assert.AreEqual(2, copied[0].Events[0].Id);
        Assert.AreEqual(3, copied[0].Events[1].Id);
    }

    [TestMethod]
    public void Copy_WithWindow_DropsChunksThatFallEntirelyBeforeTheCutoff()
    {
        var chunk = new Chunk(4);
        chunk.TryWrite(new TraceEvent(1, 10, 100, TraceEventKind.Instant, 0, 0, 1, 10));

        var captures = new[] { new ChunkCapture(chunk, chunk.Version, chunk.Count) };
        var copied = SnapshotBuilder.Copy(captures, 500, out var discarded);

        Assert.IsEmpty(copied);
        Assert.AreEqual(0, discarded);
    }

    [TestMethod]
    public void Copy_DiscardsChunksRecycledUnderneathTheCopy()
    {
        var chunk = new Chunk(4);
        chunk.TryWrite(new TraceEvent(1, 10, 100, TraceEventKind.Instant, 0, 0, 1, 10));

        var captures = new[] { new ChunkCapture(chunk, chunk.Version - 1, chunk.Count) };
        var copied = SnapshotBuilder.Copy(captures, 0, out var discarded);

        Assert.IsEmpty(copied);
        Assert.AreEqual(1, discarded);
    }

    [TestMethod]
    public async Task Copy_UnderConcurrentRotation_NeverDiscardsAChunk()
    {
        var options = new SessionOptions
        {
            ChunkCapacity = 64,
            MaxTotalChunks = 8,
            OverflowPolicy = OverflowPolicy.DropOldest
        };

        var collector = new SessionCollector(options, new ChunkPool(64), 64);
        using var stop = new CancellationTokenSource();
        using var ready = new CountdownEvent(3);

        var writers = Enumerable.Range(0, 3)
            .Select(index => Task.Run(() =>
            {
                var writer = new ThreadWriter(collector, default, index + 1);

                writer.Write(7, TraceEventKind.Instant, 0, 0);
                ready.Signal();

                while (!stop.IsCancellationRequested)
                    writer.Write(7, TraceEventKind.Instant, 0, 0);
            }))
            .ToArray();

        try
        {
            Assert.IsTrue(ready.Wait(TimeSpan.FromSeconds(30)));

            for (var i = 0; i < 200; i++)
            {
                var captures = collector.BeginSnapshot();

                try
                {
                    var copied = SnapshotBuilder.Copy(captures, 0, out var discarded);
                    collector.RecordSnapshotDiscard(discarded);

                    foreach (var chunk in copied)
                        for (var e = 0; e < chunk.Count; e++)
                            Assert.AreEqual(7, chunk.Events[e].Id);
                }
                finally
                {
                    collector.EndSnapshot();
                }
            }
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(writers);
        }

        Assert.AreEqual(0L, collector.SnapshotDiscardedChunks);
    }
}
