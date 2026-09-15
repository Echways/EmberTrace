using System.Diagnostics;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Sessions;

[TestClass]
public class SessionClockTests
{
    [TestMethod]
    public void Stop_RecordsWhenTheSessionStarted()
    {
        using var tracing = new TracingSession();

        var before = DateTimeOffset.UtcNow;
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
        var after = DateTimeOffset.UtcNow;
        var stopped = tracing.Stop();

        Assert.IsNotNull(stopped.StartedAtUtc);
        Assert.IsTrue(stopped.StartedAtUtc >= before && stopped.StartedAtUtc <= after);
    }

    [TestMethod]
    public void FullSnapshot_SharesTheSessionAnchor()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
        tracing.Instant(1);

        var snapshot = tracing.Snapshot();
        var stopped = tracing.Stop();

        Assert.AreEqual(stopped.StartTimestamp, snapshot.StartTimestamp);
        Assert.AreEqual(stopped.StartedAtUtc, snapshot.StartedAtUtc);
    }

    [TestMethod]
    public void WindowedSnapshot_ShiftsTheAnchorToItsFirstTimestamp()
    {
        using var tracing = new TracingSession();
        tracing.Start(new SessionOptions { ChunkCapacity = 1024 });
        Thread.Sleep(250);
        tracing.Instant(1);

        var snapshot = tracing.Snapshot(TimeSpan.FromMilliseconds(50));
        var stopped = tracing.Stop();

        var expected = stopped.StartedAtUtc!.Value
                       + Stopwatch.GetElapsedTime(stopped.StartTimestamp, snapshot.StartTimestamp);

        Assert.IsGreaterThan(stopped.StartTimestamp, snapshot.StartTimestamp);
        Assert.AreEqual(expected, snapshot.StartedAtUtc);
    }

    [TestMethod]
    public void FromEvents_HasNoAnchor()
    {
        Assert.IsNull(TraceSession.FromEvents([], 0, 0, 1_000_000).StartedAtUtc);
    }
}
