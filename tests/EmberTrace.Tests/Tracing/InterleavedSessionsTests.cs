using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
public class InterleavedSessionsTests
{
    private const int FirstId = 101;
    private const int SecondId = 202;

    [TestMethod]
    public void InterleavedOnOneThread_KeepsEventsInOwnSession()
    {
        using var first = new TracingSession();
        using var second = new TracingSession();

        first.Start(new SessionOptions { ChunkCapacity = 1024 });
        second.Start(new SessionOptions { ChunkCapacity = 1024 });

        const int iterations = 500;
        for (var i = 0; i < iterations; i++)
        {
            first.Instant(FirstId);
            second.Instant(SecondId);
        }

        var firstTrace = first.Stop();
        var secondTrace = second.Stop();

        Assert.AreEqual(iterations, firstTrace.EventCount);
        Assert.AreEqual(iterations, secondTrace.EventCount);
        Assert.IsTrue(ToIdSet(firstTrace).SetEquals(new[] { FirstId }));
        Assert.IsTrue(ToIdSet(secondTrace).SetEquals(new[] { SecondId }));
    }

    [TestMethod]
    public void InterleavedOnOneThread_ReusesWritersInsteadOfRentingChunkPerEvent()
    {
        const int iterations = 2000;

        RunInterleaved(16);
        var allocated = RunInterleaved(iterations);

        Assert.IsLessThan(2 * 1024 * 1024, allocated,
            $"interleaved sessions allocated {allocated} bytes for {iterations * 2} events");
    }

    private static long RunInterleaved(int iterations)
    {
        using var first = new TracingSession();
        using var second = new TracingSession();

        first.Start(new SessionOptions { ChunkCapacity = 1024 });
        second.Start(new SessionOptions { ChunkCapacity = 1024 });

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < iterations; i++)
        {
            first.Instant(FirstId);
            second.Instant(SecondId);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(iterations, first.Stop().EventCount);
        Assert.AreEqual(iterations, second.Stop().EventCount);

        return allocated;
    }

    private static HashSet<int> ToIdSet(TraceSession session)
    {
        var ids = new HashSet<int>();
        foreach (var e in session.EnumerateEvents())
            ids.Add(e.Id);
        return ids;
    }
}