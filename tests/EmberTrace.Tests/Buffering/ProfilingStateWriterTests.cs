using EmberTrace.Internal;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class ProfilingStateWriterTests
{
    private static ProfilingState CreateState()
    {
        var options = new SessionOptions { ChunkCapacity = 1024 };
        var collector = new SessionCollector(options, new ChunkPool(options.ChunkCapacity), options.ChunkCapacity);
        return new ProfilingState(options, collector, TraceMetadata.CreateDefault(), null, default, 0);
    }

    [TestMethod]
    public void GetWriter_SameThread_ReturnsSameInstance()
    {
        var state = CreateState();

        var first = state.GetWriter();

        for (var i = 0; i < 100; i++)
            Assert.AreSame(first, state.GetWriter());

        Assert.HasCount(1, state.Writers.ToArray());
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
        var state = CreateState();

        const int tasks = 8;
        const int callsPerTask = 500;

        var observed = await Task.WhenAll(Enumerable.Range(0, tasks).Select(_idx => Task.Run(() =>
        {
            var writer = state.GetWriter();
            for (var i = 0; i < callsPerTask; i++)
                Assert.AreSame(writer, state.GetWriter());

            return (Thread: Environment.CurrentManagedThreadId, Writer: writer);
        })));

        var threads = new HashSet<int>(observed.Select(o => o.Thread));
        var writers = new HashSet<ThreadWriter>(observed.Select(o => o.Writer));

        Assert.HasCount(threads.Count, writers);
        Assert.HasCount(threads.Count, state.Writers.ToArray());
    }

    [TestMethod]
    public void WriteAt_UsesTheSuppliedTimestamp()
    {
        var options = new SessionOptions { ChunkCapacity = 1024 };
        var collector = new SessionCollector(options, new ChunkPool(options.ChunkCapacity), options.ChunkCapacity);
        var writer = new ThreadWriter(collector, new SamplingPolicy(0, null, 0), 1);

        writer.WriteAt(42, TraceEventKind.Begin, 0, 0, 123_456);
        writer.WriteAt(42, TraceEventKind.End, 0, 0, 123_999);

        var chunk = collector.Chunks.Single();

        Assert.AreEqual(2, chunk.Count);
        Assert.AreEqual(123_456, chunk.Events[0].Timestamp);
        Assert.AreEqual(123_999, chunk.Events[1].Timestamp);
    }

    [TestMethod]
    public void Write_StillUsesTheCurrentClock()
    {
        var options = new SessionOptions { ChunkCapacity = 1024 };
        var collector = new SessionCollector(options, new ChunkPool(options.ChunkCapacity), options.ChunkCapacity);
        var writer = new ThreadWriter(collector, new SamplingPolicy(0, null, 0), 1);

        var before = Timestamp.Now();
        writer.Write(42, TraceEventKind.Instant, 0, 0);
        var after = Timestamp.Now();

        var recorded = collector.Chunks.Single().Events[0].Timestamp;

        Assert.IsTrue(recorded >= before && recorded <= after,
            $"timestamp {recorded} must fall inside [{before}, {after}]");
    }
}
