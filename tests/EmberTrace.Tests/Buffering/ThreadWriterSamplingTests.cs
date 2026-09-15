using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class ThreadWriterSamplingTests
{
    [TestMethod]
    [DataRow(0, 20, 20, 0)]
    [DataRow(1, 9, 9, 0)]
    [DataRow(2, 8, 4, 4)]
    [DataRow(3, 9, 3, 6)]
    [DataRow(5, 10, 2, 8)]
    public void GlobalEveryN_KeepsEveryNthEvent(int everyN, int total, int expectedKept, int expectedSampledOut)
    {
        var session = Record(new SessionOptions { SampleEveryNGlobal = everyN }, s =>
        {
            for (var i = 0; i < total; i++)
                s.Instant(42);
        });

        Assert.AreEqual(expectedKept, session.EventCount);
        Assert.AreEqual(expectedSampledOut, session.SampledOutEvents);
        Assert.AreEqual(0L, session.DroppedEvents);
    }

    [TestMethod]
    public void EveryNById_SamplesEachIdIndependently()
    {
        var session = Record(new SessionOptions
        {
            SampleEveryNById = new Dictionary<int, int> { [1] = 2, [2] = 3 }
        }, s =>
        {
            for (var i = 0; i < 5; i++)
            {
                s.Instant(1);
                s.Instant(2);
                s.Instant(3);
            }
        });

        var events = session.Events();

        Assert.AreEqual(3, events.Count(e => e.Id == 1));
        Assert.AreEqual(2, events.Count(e => e.Id == 2));
        Assert.AreEqual(5, events.Count(e => e.Id == 3));
        Assert.AreEqual(5L, session.SampledOutEvents);
    }

    [TestMethod]
    [DataRow(10, 12, 3L)]
    [DataRow(1, 9, 3L)]
    public void EveryNById_OverridesGlobalForTheMatchingIdAndLeavesOthersOnGlobal(
        int sampledId, int events, long expectedKept)
    {
        var session = Record(new SessionOptions
        {
            SampleEveryNById = new Dictionary<int, int> { [10] = 4 },
            SampleEveryNGlobal = 3
        }, s =>
        {
            for (var i = 0; i < events; i++)
                s.Instant(sampledId);
        });

        Assert.AreEqual(expectedKept, session.EventCount);
    }

    [TestMethod]
    [DataRow(1, 1, 1, 0)]
    [DataRow(5, 5, 5, 0)]
    [DataRow(3, 8, 3, 5)]
    [DataRow(7, 12, 7, 5)]
    public void MaxEventsPerSecond_AcceptsTheLimitAndDropsTheExcess(
        int limit, int total, int expectedKept, int expectedDropped)
    {
        var session = Record(new SessionOptions { MaxEventsPerSecond = limit }, s =>
        {
            for (var i = 0; i < total; i++)
                s.Instant(1);
        });

        Assert.AreEqual(expectedKept, session.EventCount);
        Assert.AreEqual(expectedDropped, session.DroppedEvents);
        Assert.AreEqual(expectedDropped > 0, session.WasOverflow);
    }

    [TestMethod]
    public void MaxEventsPerSecond_OpensANewWindowExactlyOneSecondLater()
    {
        const long start = 1_000;

        var collector = Collectors.Create(capacity: 1024);
        var writer = new ThreadWriter(collector, new SamplingPolicy(0, null, 3), 1);

        for (var i = 0; i < 3; i++)
            writer.WriteAt(1, TraceEventKind.Instant, 0, 0, start);

        writer.WriteAt(1, TraceEventKind.Instant, 0, 0, start + Timestamp.Frequency - 1);

        for (var i = 0; i < 4; i++)
            writer.WriteAt(1, TraceEventKind.Instant, 0, 0, start + Timestamp.Frequency);

        Assert.AreEqual(6, collector.Chunks.Single().Count);
        Assert.AreEqual(2L, collector.DroppedEvents);
    }

    [TestMethod]
    public void MaxEventsPerSecond_WithStopSession_ClosesOnTheFirstExcessEvent()
    {
        const int limit = 2;

        var session = Record(new SessionOptions
        {
            MaxEventsPerSecond = limit,
            OverflowPolicy = OverflowPolicy.StopSession
        }, s =>
        {
            for (var i = 0; i < limit + 5; i++)
                s.Instant(1);
        });

        Assert.AreEqual(limit, session.EventCount);
        Assert.AreEqual(1L, session.DroppedEvents);
        Assert.IsTrue(session.WasOverflow);
    }

    [TestMethod]
    public void GlobalEveryN_OneEventPerThread_KeepsTheGlobalRatio()
    {
        const int everyN = 8;
        const int threads = 64;

        var session = Record(new SessionOptions { SampleEveryNGlobal = everyN },
            s => RunOnThreads(threads, () => s.Instant(42)));

        Assert.AreEqual(threads / everyN, session.EventCount);
    }

    [TestMethod]
    public void EveryNById_OneEventPerThread_KeepsTheGlobalRatio()
    {
        const int everyN = 8;
        const int threads = 64;

        var session = Record(new SessionOptions { SampleEveryNById = new Dictionary<int, int> { [7] = everyN } },
            s => RunOnThreads(threads, () => s.Instant(7)));

        Assert.AreEqual(threads / everyN, session.EventCount);
    }

    [TestMethod]
    public void GlobalEveryN_ManyEventsPerThread_KeepsTheGlobalRatioWithinOneTicketBlockPerThread()
    {
        const int everyN = 10;
        const int threads = 8;
        const int perThread = 1000;

        var session = Record(new SessionOptions { SampleEveryNGlobal = everyN, ChunkCapacity = 4096 },
            s => RunOnThreads(threads, () =>
            {
                for (var i = 0; i < perThread; i++)
                    s.Instant(42);
            }));

        var expected = threads * perThread / everyN;
        var slack = threads * SampleTicketPool.BlockSize / everyN;

        Assert.IsGreaterThanOrEqualTo(expected - slack, session.EventCount);
        Assert.IsLessThanOrEqualTo(expected + slack, session.EventCount);
        Assert.AreEqual(threads * perThread - session.EventCount, session.SampledOutEvents);
    }

    private static TraceSession Record(SessionOptions options, Action<TracingSession> body)
    {
        using var tracing = new TracingSession();
        tracing.Start(options);
        body(tracing);
        return tracing.Stop();
    }

    private static void RunOnThreads(int count, Action body)
    {
        var threads = Enumerable.Range(0, count).Select(_ => new Thread(() => body()) { IsBackground = true }).ToArray();

        foreach (var thread in threads)
            thread.Start();

        foreach (var thread in threads)
            thread.Join();
    }
}
