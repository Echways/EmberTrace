using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Buffering;

[TestClass]
public class ThreadWriterSamplingTests
{
    [TestMethod]
    [DataRow(1, 9, 9, 0)]
    [DataRow(2, 8, 4, 4)]
    [DataRow(3, 9, 3, 6)]
    [DataRow(5, 10, 2, 8)]
    public void GlobalEveryN_CorrectPassThroughRatio(
        int everyN, int totalEvents, int expectedPassed, int expectedSampledOut)
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions { SampleEveryNGlobal = everyN, ChunkCapacity = 1024 });

        for (var i = 0; i < totalEvents; i++)
            ts.Instant(42);

        var session = ts.Stop();

        Assert.AreEqual(expectedPassed, session.EventCount,
            $"everyN={everyN}: expected {expectedPassed} events to pass through");
        Assert.AreEqual(expectedSampledOut, session.SampledOutEvents,
            $"everyN={everyN}: expected {expectedSampledOut} events sampled out");
    }

    [TestMethod]
    public void GlobalEveryN_Zero_TreatedAsDisabled_AllEventsPass()
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions { SampleEveryNGlobal = 0, ChunkCapacity = 64 });

        for (var i = 0; i < 20; i++)
            ts.Instant(1);

        var session = ts.Stop();

        Assert.AreEqual(20L, session.EventCount);
        Assert.AreEqual(0L, session.SampledOutEvents);
    }

    [TestMethod]
    public void EveryNById_DifferentRatesPerId_AreIndependent()
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions
        {
            SampleEveryNById = new Dictionary<int, int>
            {
                [1] = 2,
                [2] = 3
            },
            SampleEveryNGlobal = 1,
            ChunkCapacity = 512
        });

        for (var i = 0; i < 5; i++) ts.Instant(1);
        for (var i = 0; i < 5; i++) ts.Instant(2);
        for (var i = 0; i < 5; i++) ts.Instant(3);

        var session = ts.Stop();
        var events = ToArray(session);

        Assert.AreEqual(3, events.Count(e => e.Id == 1), "id=1, everyN=2: tickets 0,2,4 should pass");
        Assert.AreEqual(2, events.Count(e => e.Id == 2), "id=2, everyN=3: tickets 0,3 should pass");
        Assert.AreEqual(5, events.Count(e => e.Id == 3), "id=3, no per-id rule: all should pass");
    }

    [TestMethod]
    public void EveryNById_IdNotInDict_FallsBackToGlobalEveryN()
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions
        {
            SampleEveryNById = new Dictionary<int, int> { [999] = 2 },
            SampleEveryNGlobal = 3,
            ChunkCapacity = 512
        });

        for (var i = 0; i < 9; i++)
            ts.Instant(1);

        var session = ts.Stop();

        Assert.AreEqual(3L, session.EventCount);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(10)]
    public void MaxEventsPerSecond_WithinRateWindow_AcceptsExactLimit(int rateLimit)
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions { MaxEventsPerSecond = rateLimit, ChunkCapacity = 1024 });

        for (var i = 0; i < rateLimit; i++)
            ts.Instant(1);

        var session = ts.Stop();

        Assert.AreEqual(rateLimit, session.EventCount,
            $"All {rateLimit} events within rate limit should be accepted");
        Assert.AreEqual(0L, session.DroppedEvents);
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(7)]
    public void MaxEventsPerSecond_ExcessEvents_AreDropped(int rateLimit)
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions { MaxEventsPerSecond = rateLimit, ChunkCapacity = 1024 });

        for (var i = 0; i < rateLimit + 5; i++)
            ts.Instant(1);

        var session = ts.Stop();

        Assert.AreEqual(rateLimit, session.EventCount,
            $"Only {rateLimit} events should pass the rate gate");
        Assert.AreEqual(5L, session.DroppedEvents, "Excess events must be counted as dropped");
    }

    [TestMethod]
    public void MaxEventsPerSecond_NewWindowAfterOneSec_ResetsCounter()
    {
        const int rateLimit = 3;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { MaxEventsPerSecond = rateLimit, ChunkCapacity = 1024 });

        for (var i = 0; i < rateLimit; i++)
            ts.Instant(1);

        for (var i = 0; i < 2; i++)
            ts.Instant(1);

        Thread.Sleep(1100);

        for (var i = 0; i < rateLimit; i++)
            ts.Instant(1);

        var session = ts.Stop();

        Assert.AreEqual(rateLimit * 2, session.EventCount,
            "Two full windows should each contribute rateLimit events");
        Assert.AreEqual(2L, session.DroppedEvents,
            "Only the 2 excess events from the first window should be dropped");
    }

    [TestMethod]
    public void MaxEventsPerSecond_StopSession_ClosesAfterRateLimitExceeded()
    {
        const int rateLimit = 2;

        var ts = new TracingSession();
        ts.Start(new SessionOptions
        {
            MaxEventsPerSecond = rateLimit,
            OverflowPolicy = OverflowPolicy.StopSession,
            ChunkCapacity = 1024
        });

        for (var i = 0; i < rateLimit + 5; i++)
            ts.Instant(1);

        var session = ts.Stop();

        Assert.AreEqual(rateLimit, session.EventCount);
        Assert.IsTrue(session.WasOverflow);
    }

    [TestMethod]
    public void EveryNById_TakesPriorityOverGlobal_ForMatchingId()
    {
        var ts = new TracingSession();
        ts.Start(new SessionOptions
        {
            SampleEveryNById = new Dictionary<int, int> { [10] = 4 },
            SampleEveryNGlobal = 2,
            ChunkCapacity = 512
        });

        for (var i = 0; i < 12; i++)
            ts.Instant(10);

        var session = ts.Stop();

        Assert.AreEqual(3L, session.EventCount,
            "Per-id everyN=4 should pass tickets 0, 4, 8");
    }

    [TestMethod]
    public void GlobalEveryN_OneEventPerThread_KeepsGlobalRatio()
    {
        const int everyN = 8;
        const int threads = 64;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { SampleEveryNGlobal = everyN, ChunkCapacity = 1024 });

        RunOnThreads(threads, () => ts.Instant(42));

        var session = ts.Stop();

        Assert.AreEqual(threads / everyN, session.EventCount,
            "Short-lived threads must share one global ticket sequence, not each keep their first event");
    }

    [TestMethod]
    public void GlobalEveryN_ManyEventsPerThread_KeepsGlobalRatio()
    {
        const int everyN = 10;
        const int threads = 8;
        const int perThread = 1000;

        var ts = new TracingSession();
        ts.Start(new SessionOptions { SampleEveryNGlobal = everyN, ChunkCapacity = 4096 });

        RunOnThreads(threads, () =>
        {
            for (var i = 0; i < perThread; i++)
                ts.Instant(42);
        });

        var session = ts.Stop();

        var expected = threads * perThread / everyN;
        var slack = threads * SampleTicketPool.BlockSize / everyN;

        Assert.IsGreaterThanOrEqualTo(expected - slack, session.EventCount);
        Assert.IsLessThanOrEqualTo(expected + slack, session.EventCount);
        Assert.AreEqual(threads * perThread - session.EventCount, session.SampledOutEvents);
    }

    [TestMethod]
    public void EveryNById_OneEventPerThread_KeepsGlobalRatio()
    {
        const int everyN = 8;
        const int threads = 64;

        var ts = new TracingSession();
        ts.Start(new SessionOptions
        {
            SampleEveryNById = new Dictionary<int, int> { [7] = everyN },
            ChunkCapacity = 1024
        });

        RunOnThreads(threads, () => ts.Instant(7));

        var session = ts.Stop();

        Assert.AreEqual(threads / everyN, session.EventCount);
    }

    private static void RunOnThreads(int count, Action body)
    {
        var threads = new Thread[count];
        for (var i = 0; i < count; i++)
        {
            threads[i] = new Thread(() => body()) { IsBackground = true };
            threads[i].Start();
        }

        foreach (var thread in threads)
            thread.Join();
    }

    private static TraceEventRecord[] ToArray(TraceSession session)
    {
        var list = new List<TraceEventRecord>();
        foreach (var e in session.EnumerateEvents())
            list.Add(e);
        return list.ToArray();
    }
}