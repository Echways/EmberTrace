using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Sessions;

[TestClass]
public class TraceSessionFactoryTests
{
    [TestMethod]
    [DataRow(1_000_000L, 1_000_000L, 500.0)]
    [DataRow(0L, 0L, 0.0)]
    public void TimestampFrequency_FallsBackToTheStopwatchWhenNotPositive(
        long frequency, long expectedFrequency, double expectedDurationMs)
    {
        var session = new TraceSession(
            [new Chunk(1)], 0, frequency / 2, new SessionOptions(), new Dictionary<int, string>(),
            0, 0, 0, false, null, frequency);

        Assert.AreEqual(expectedFrequency == 0 ? Timestamp.Frequency : expectedFrequency, session.TimestampFrequency);
        Assert.AreEqual(expectedDurationMs, session.DurationMs, 0.001);
    }

    [TestMethod]
    public void FromEvents_PreservesEventsAndSessionCounters()
    {
        var events = new[]
        {
            new TraceEventRecord(1, 7, 100, TraceEventKind.Begin, 0, 0, 1, 3),
            new TraceEventRecord(1, 7, 200, TraceEventKind.End, 0, 0, 2, 3)
        };

        var session = TraceSession.FromEvents(
            events, 100, 200, 1_000_000,
            new Dictionary<int, string> { [7] = "worker" },
            droppedEvents: 5, droppedChunks: 1, sampledOutEvents: 9, wasOverflow: true, isSnapshot: true);

        Assert.AreEqual(100L, session.StartTimestamp);
        Assert.AreEqual(200L, session.EndTimestamp);
        Assert.AreEqual(1_000_000L, session.TimestampFrequency);
        Assert.AreEqual(5L, session.DroppedEvents);
        Assert.AreEqual(1L, session.DroppedChunks);
        Assert.AreEqual(9L, session.SampledOutEvents);
        Assert.IsTrue(session.WasOverflow);
        Assert.IsTrue(session.IsSnapshot);
        Assert.AreEqual("worker", session.ThreadNames[7]);
        CollectionAssert.AreEqual(events, session.SortedEvents());
    }

    [TestMethod]
    public void FromEvents_SpansMultipleChunksAndKeepsEveryEvent()
    {
        var events = Enumerable.Range(0, 3000)
            .Select(i => new TraceEventRecord(i, 1, i, TraceEventKind.Instant, 0, 0, i + 1))
            .ToArray();

        var session = TraceSession.FromEvents(
            events, 0, 3000, 1_000_000, options: new SessionOptions { ChunkCapacity = 1024 });

        Assert.AreEqual(3000L, session.EventCount);
        CollectionAssert.AreEqual(events, session.SortedEvents());
    }

    [TestMethod]
    public void FromEvents_WithNullEvents_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => TraceSession.FromEvents(null!, 0, 0));
    }

    [TestMethod]
    public void EnumerateEventsSorted_MergesChunksByTimestampThenTrackThenSequence()
    {
        var session = new TraceSession(
            [
                Chunk(Event(3, 10, 1, 2), Event(2, 10, 2, 2), Event(1, 20, 3, 2)),
                Chunk(Event(4, 10, 1, 1), Event(5, 30, 2, 1))
            ],
            0, 30, new SessionOptions(), new Dictionary<int, string>(), 0, 0, 0, false);

        CollectionAssert.AreEqual(new[] { 4, 3, 2, 1, 5 }, session.SortedEvents().Select(e => e.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 3, 2, 1, 4, 5 }, session.Events().Select(e => e.Id).ToArray());
    }

    private static Chunk Chunk(params TraceEvent[] events)
    {
        var chunk = new Chunk(events.Length);
        foreach (var e in events)
            chunk.TryWrite(e);

        return chunk;
    }

    private static TraceEvent Event(int id, long timestamp, long sequence, int trackId)
    {
        return new TraceEvent(id, 1, timestamp, TraceEventKind.Instant, 0, 0, sequence, trackId);
    }
}
