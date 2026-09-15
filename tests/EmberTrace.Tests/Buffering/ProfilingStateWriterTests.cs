using EmberTrace.Internal;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class ProfilingStateWriterTests
{
    [TestMethod]
    public void GetWriter_SameThread_ReturnsSameInstance()
    {
        var state = CreateState();

        var first = state.GetWriter();

        for (var i = 0; i < 100; i++)
            Assert.AreSame(first, state.GetWriter());

        Assert.HasCount(1, state.Writers);
    }

    [TestMethod]
    public void GetWriter_DistinctStates_DoNotShareWriters()
    {
        var first = CreateState();
        var second = CreateState();

        Assert.AreNotSame(first.GetWriter(), second.GetWriter());
        Assert.AreNotEqual(first.Id, second.Id);
    }

    [TestMethod]
    public async Task GetWriter_OneWriterPerThread_UnderConcurrency()
    {
        const int tasks = 8;
        const int callsPerTask = 500;

        var state = CreateState();

        var observed = await Task.WhenAll(Enumerable.Range(0, tasks).Select(_ => Task.Run(() =>
        {
            var writer = state.GetWriter();
            for (var i = 0; i < callsPerTask; i++)
                Assert.AreSame(writer, state.GetWriter());

            return (Thread: Environment.CurrentManagedThreadId, Writer: writer);
        })));

        var threads = observed.Select(o => o.Thread).Distinct().Count();

        Assert.HasCount(threads, observed.Select(o => o.Writer).Distinct());
        Assert.HasCount(threads, state.Writers);
    }

    [TestMethod]
    public void GetWriter_AssignsDistinctTrackIdsPerThread()
    {
        var state = CreateState();
        state.GetWriter().Write(1, TraceEventKind.Instant, 0, 0);

        var worker = new Thread(() => state.GetWriter().Write(1, TraceEventKind.Instant, 0, 0));
        worker.Start();
        worker.Join();

        var tracks = state.Collector.Chunks.Select(c => c.Events[0].TrackId).ToArray();

        Assert.HasCount(2, tracks);
        Assert.AreNotEqual(tracks[0], tracks[1]);
    }

    [TestMethod]
    public void WriteAt_UsesTheSuppliedTimestamp()
    {
        var collector = Collectors.Create(capacity: 1024);
        var writer = new ThreadWriter(collector, default, 1);

        writer.WriteAt(42, TraceEventKind.Begin, 0, 0, 123_456);
        writer.WriteAt(42, TraceEventKind.End, 0, 0, 123_999);

        var chunk = collector.Chunks.Single();

        Assert.AreEqual(2, chunk.Count);
        Assert.AreEqual(123_456, chunk.Events[0].Timestamp);
        Assert.AreEqual(123_999, chunk.Events[1].Timestamp);
    }

    [TestMethod]
    public void Write_StampsTheCurrentClockAndIncrementsTheSequence()
    {
        var collector = Collectors.Create(capacity: 1024);
        var writer = new ThreadWriter(collector, default, 5);

        var before = Timestamp.Now();
        writer.Write(42, TraceEventKind.Instant, 0, 0);
        writer.Write(42, TraceEventKind.Instant, 0, 0);
        var after = Timestamp.Now();

        var events = collector.Chunks.Single().Events;

        Assert.IsGreaterThanOrEqualTo(before, events[0].Timestamp);
        Assert.IsLessThanOrEqualTo(after, events[1].Timestamp);
        Assert.AreEqual(1L, events[0].Sequence);
        Assert.AreEqual(2L, events[1].Sequence);
        Assert.AreEqual(5, events[0].TrackId);
    }

    [TestMethod]
    public void Write_AfterDrainAndDetach_IsIgnored()
    {
        var collector = Collectors.Create(capacity: 1024);
        var writer = new ThreadWriter(collector, default, 1);
        writer.Write(1, TraceEventKind.Instant, 0, 0);

        writer.DrainAndDetach();
        writer.Write(1, TraceEventKind.Instant, 0, 0);

        Assert.AreEqual(1, collector.Chunks.Single().Count);
    }

    private static ProfilingState CreateState()
    {
        var options = new SessionOptions { ChunkCapacity = 1024 };
        var collector = new SessionCollector(options, new ChunkPool(options.ChunkCapacity), options.ChunkCapacity);
        return new ProfilingState(options, collector, TraceMetadata.CreateDefault(), null, default, 0,
            DateTimeOffset.UnixEpoch);
    }
}
