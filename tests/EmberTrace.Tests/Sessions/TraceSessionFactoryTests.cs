using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Sessions;

[TestClass]
public class TraceSessionFactoryTests
{
    [TestMethod]
    public void TimestampFrequency_WhenExplicit_IsUsedForDurations()
    {
        var chunk = new Chunk(1);

        var session = new TraceSession(
            new[] { chunk }, 0, 500_000, new SessionOptions(), new Dictionary<int, string>(),
            0, 0, 0, false, null, 1_000_000);

        Assert.AreEqual(1_000_000, session.TimestampFrequency);
        Assert.AreEqual(500.0, session.DurationMs, 0.001);
    }

    [TestMethod]
    public void TimestampFrequency_WhenZero_FallsBackToStopwatchFrequency()
    {
        var chunk = new Chunk(1);

        var session = new TraceSession(
            new[] { chunk }, 0, 0, new SessionOptions(), new Dictionary<int, string>(),
            0, 0, 0, false);

        Assert.AreEqual(Timestamp.Frequency, session.TimestampFrequency);
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
            droppedEvents: 5, droppedChunks: 1, sampledOutEvents: 9, wasOverflow: true);

        Assert.AreEqual(2, session.EventCount);
        Assert.AreEqual(1_000_000, session.TimestampFrequency);
        Assert.AreEqual(5, session.DroppedEvents);
        Assert.AreEqual(1, session.DroppedChunks);
        Assert.AreEqual(9, session.SampledOutEvents);
        Assert.IsTrue(session.WasOverflow);
        Assert.AreEqual("worker", session.ThreadNames[7]);

        var roundTripped = Sorted(session);

        Assert.HasCount(2, roundTripped);
        Assert.AreEqual(TraceEventKind.Begin, roundTripped[0].Kind);
        Assert.AreEqual(3, roundTripped[0].TrackId);
        Assert.AreEqual(TraceEventKind.End, roundTripped[1].Kind);
    }

    [TestMethod]
    public void FromEvents_SpansMultipleChunksWhenCapacityExceeded()
    {
        var events = new List<TraceEventRecord>();
        for (var i = 0; i < 3000; i++)
            events.Add(new TraceEventRecord(i, 1, i, TraceEventKind.Instant, 0, 0, i + 1));

        var session = TraceSession.FromEvents(
            events, 0, 3000, 1_000_000, options: new SessionOptions { ChunkCapacity = 1024 });

        Assert.AreEqual(3000, session.EventCount);
        Assert.HasCount(3000, Sorted(session));
    }

    [TestMethod]
    public void FromEntries_BuildsLookupProvider()
    {
        var provider = TraceMetadata.FromEntries(new[]
        {
            new TraceMeta(10, "Load", "App"),
            new TraceMeta(20, "Parse", null)
        });

        Assert.IsTrue(provider.TryGet(10, out var load));
        Assert.AreEqual("Load", load.Name);
        Assert.AreEqual("App", load.Category);

        Assert.IsTrue(provider.TryGet(20, out var parse));
        Assert.IsNull(parse.Category);

        Assert.IsFalse(provider.TryGet(30, out _));
    }

    private static TraceEventRecord[] Sorted(TraceSession session)
    {
        var list = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEventsSorted())
            list.Add(e);
        return list.ToArray();
    }
}
