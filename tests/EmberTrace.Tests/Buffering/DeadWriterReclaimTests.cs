using EmberTrace.Internal;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class DeadWriterReclaimTests
{
    private const int FinishedId = 2;
    private const int LiveId = 3;

    [TestMethod]
    public void Retention_ExpiresChunksOfFinishedThreads()
    {
        long now = 1_000;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, capacity: 16,
            retention: TimeSpan.FromSeconds(1), clock: () => now);

        RunToCompletion(() =>
            new ThreadWriter(collector, default, 1).WriteAt(FinishedId, TraceEventKind.Instant, 0, 0, 1_000));

        Assert.HasCount(1, collector.Chunks);
        Assert.AreEqual(1, collector.WriterCount);

        now += 3 * Timestamp.Frequency;
        collector.BeginSnapshot();
        collector.EndSnapshot();

        Assert.IsEmpty(collector.Chunks);
        Assert.AreEqual(0, collector.WriterCount);
        Assert.AreEqual(1L, collector.DroppedChunks);
    }

    [TestMethod]
    public void Retention_SweepsFinishedThreadsAtMostOncePerInterval()
    {
        long now = 1_000;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, capacity: 16,
            retention: TimeSpan.FromSeconds(1), clock: () => now);

        RunToCompletion(() => new ThreadWriter(collector, default, 1));

        collector.BeginSnapshot();
        collector.EndSnapshot();
        Assert.AreEqual(1, collector.WriterCount);

        now += Timestamp.Frequency;
        collector.BeginSnapshot();
        collector.EndSnapshot();
        Assert.AreEqual(0, collector.WriterCount);
    }

    [TestMethod]
    public void DropOldest_AtTheChunkLimit_ReclaimsChunksOfFinishedThreads()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions
        {
            ChunkCapacity = 1024,
            MaxTotalChunks = 4,
            OverflowPolicy = OverflowPolicy.DropOldest
        });

        for (var i = 0; i < 4; i++)
            RunToCompletion(() =>
            {
                using (tracing.Scope(FinishedId))
                {
                }
            });

        for (var i = 0; i < 5000; i++)
            using (tracing.Scope(LiveId))
            {
            }

        var session = tracing.Stop();
        var events = session.SortedEvents();

        Assert.IsGreaterThan(2048, events.Count(e => e.Id == LiveId));
        Assert.IsLessThanOrEqualTo(4 * 1024L, session.EventCount);
        Assert.AreEqual(LiveId, events[^1].Id);
        Assert.AreEqual(TraceEventKind.End, events[^1].Kind);
    }

    [TestMethod]
    public void DropNew_AtTheChunkLimit_KeepsEventsOfFinishedThreads()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions
        {
            ChunkCapacity = 1024,
            MaxTotalChunks = 2,
            OverflowPolicy = OverflowPolicy.DropNew
        });

        for (var i = 0; i < 2; i++)
            RunToCompletion(() => tracing.Instant(FinishedId));

        tracing.Instant(LiveId);

        var session = tracing.Stop();

        Assert.AreEqual(2, session.Events().Count(e => e.Id == FinishedId));
        Assert.AreEqual(0, session.Events().Count(e => e.Id == LiveId));
        Assert.AreEqual(1L, session.DroppedEvents);
    }

    [TestMethod]
    public void DropOldest_AtTheEventLimit_EvictsChunksOfFinishedThreads()
    {
        long now = 1_000;
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxEvents: 4, capacity: 4, clock: () => now);

        RunToCompletion(() =>
        {
            var writer = new ThreadWriter(collector, default, 1);
            for (var i = 0; i < 4; i++)
                writer.WriteAt(FinishedId, TraceEventKind.Instant, 0, 0, 1_000 + i);
        });

        now += Timestamp.Frequency;

        Assert.IsTrue(collector.TryAcceptEvent());
        Assert.IsEmpty(collector.Chunks);
        Assert.AreEqual(4L, collector.DroppedEvents);
    }

    [TestMethod]
    public void DropOldest_WhenEveryChunkIsHeldByALiveWriter_GrowsPastTheLimitAndShrinksBack()
    {
        var collector = Collectors.Create(OverflowPolicy.DropOldest, maxChunks: 2, capacity: 8);

        Assert.IsTrue(collector.TryRentChunk(out var first));
        Assert.IsTrue(collector.TryRentChunk(out var second));
        Assert.IsTrue(collector.TryRentChunk(out _));

        Assert.HasCount(3, collector.Chunks);
        Assert.IsFalse(collector.WasOverflow);
        Assert.AreEqual(0L, collector.DroppedChunks);

        collector.MarkChunkInactive(first!);
        collector.MarkChunkInactive(second!);

        Assert.IsTrue(collector.TryRentChunk(out _));

        Assert.HasCount(2, collector.Chunks);
        Assert.AreEqual(2L, collector.DroppedChunks);
        Assert.IsTrue(collector.WasOverflow);
    }

    [TestMethod]
    public void SealedWriter_IsForgottenByTheProfilingState()
    {
        var options = new SessionOptions
        {
            ChunkCapacity = 1024,
            MaxTotalChunks = 1,
            OverflowPolicy = OverflowPolicy.DropOldest
        };
        var collector = new SessionCollector(options, new ChunkPool(options.ChunkCapacity), options.ChunkCapacity);
        var state = new ProfilingState(options, collector, TraceMetadata.CreateDefault(), null, default, 0,
            DateTimeOffset.UnixEpoch);

        RunToCompletion(() => state.GetWriter().Write(FinishedId, TraceEventKind.Instant, 0, 0));
        Assert.HasCount(1, state.Writers);

        state.GetWriter().Write(LiveId, TraceEventKind.Instant, 0, 0);

        Assert.HasCount(1, state.Writers);
        Assert.AreEqual(1, collector.WriterCount);

        var chunk = collector.Chunks.Single();
        Assert.AreEqual(1, chunk.Count);
        Assert.AreEqual(LiveId, chunk.Events[0].Id);
    }

    [TestMethod]
    public void Retention_AfterABurstOfThreads_KeepsOnlyABoundedPoolOfFreeChunks()
    {
        long now = 1_000;
        var pool = new ChunkPool(16, 4);
        var options = new SessionOptions
        {
            ChunkCapacity = 16,
            OverflowPolicy = OverflowPolicy.DropOldest,
            MaxRetentionWindow = TimeSpan.FromSeconds(1)
        };
        var collector = new SessionCollector(options, pool, 16, () => now);

        for (var i = 0; i < 20; i++)
            RunToCompletion(() =>
                new ThreadWriter(collector, default, 1).WriteAt(FinishedId, TraceEventKind.Instant, 0, 0, 1_000));

        now += 3 * Timestamp.Frequency;
        collector.BeginSnapshot();
        collector.EndSnapshot();

        Assert.IsEmpty(collector.Chunks);
        Assert.AreEqual(4, pool.Retained);
    }

    private static void RunToCompletion(Action body)
    {
        var thread = new Thread(() => body());
        thread.Start();
        thread.Join();
    }
}
