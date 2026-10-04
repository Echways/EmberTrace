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
                Chunk(2, (3, 10), (2, 10), (1, 20)),
                Chunk(1, (4, 10), (5, 30))
            ],
            0, 30, new SessionOptions(), new Dictionary<int, string>(), 0, 0, 0, false);

        CollectionAssert.AreEqual(new[] { 4, 3, 2, 1, 5 }, session.SortedEvents().Select(e => e.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 3, 2, 1, 4, 5 }, session.Events().Select(e => e.Id).ToArray());
    }

    [TestMethod]
    public void FromEvents_WithUnorderedEvents_StillEnumeratesSortedAndPersists()
    {
        var events = new[]
        {
            new TraceEventRecord(1, 1, 300, TraceEventKind.Instant, 0, 0, 3),
            new TraceEventRecord(2, 1, 100, TraceEventKind.Instant, 0, 0, 1),
            new TraceEventRecord(3, 2, 200, TraceEventKind.Instant, 0, 0, 1),
            new TraceEventRecord(4, 1, 100, TraceEventKind.Instant, 0, 0, 2),
            new TraceEventRecord(5, 2, 50, TraceEventKind.Instant, 0, 0, 2)
        };

        var session = TraceSession.FromEvents(events, 0, 300, 1_000_000);

        CollectionAssert.AreEqual(new[] { 5, 2, 4, 3, 1 }, session.SortedEvents().Select(e => e.Id).ToArray());
        Assert.AreEqual(5L, session.EventCount);

        using var stream = new MemoryStream();
        TraceFormat.Write(session, stream);
        stream.Position = 0;

        CollectionAssert.AreEqual(new[] { 5, 2, 4, 3, 1 },
            TraceFormat.Read(stream).SortedEvents().Select(e => e.Id).ToArray());
    }

    [TestMethod]
    public void FromEvents_WithInterleavedThreadsAndArbitrarySequences_KeepsEveryFieldOfEveryEvent()
    {
        var events = new[]
        {
            new TraceEventRecord(1, 1, 100, TraceEventKind.Instant, 0, 0, 0, 1),
            new TraceEventRecord(2, 2, 110, TraceEventKind.Counter, 0, 9, 40, 5),
            new TraceEventRecord(3, 1, 120, TraceEventKind.Instant, 0, 0, 0, 1),
            new TraceEventRecord(4, 2, 130, TraceEventKind.FlowStart, 77, 0, 41, 5),
            new TraceEventRecord(5, 1, 140, TraceEventKind.Instant, 0, 0, 12, 1)
        };

        var session = TraceSession.FromEvents(events, 100, 140, 1_000_000);

        CollectionAssert.AreEqual(events, session.SortedEvents());
        CollectionAssert.AreEquivalent(events, session.Events());
    }

    private static Chunk Chunk(int trackId, params (int Id, long Timestamp)[] events)
    {
        return new Chunk(
            events.Select(e => new TraceEvent(e.Id, e.Timestamp, TraceEventKind.Instant, 0, 0)).ToArray(),
            1, trackId, 1);
    }
}
