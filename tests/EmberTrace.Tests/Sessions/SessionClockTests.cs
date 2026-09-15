using System.Diagnostics;
using EmberTrace.Sessions;

namespace EmberTrace.Tests.Sessions;

[TestClass]
public class SessionClockTests
{
    [TestMethod]
    public void Stop_RecordsWhenTheSessionStarted()
    {
        using var session = new TracingSession();

        var before = DateTimeOffset.UtcNow;
        session.Start(new SessionOptions { ChunkCapacity = 1024 });
        var after = DateTimeOffset.UtcNow;
        var stopped = session.Stop();

        Assert.IsNotNull(stopped.StartedAtUtc);
        Assert.IsTrue(stopped.StartedAtUtc >= before && stopped.StartedAtUtc <= after);
    }

    [TestMethod]
    public void WindowedSnapshot_ShiftsTheAnchorToItsFirstTimestamp()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });
        Thread.Sleep(250);
        session.Instant(1);

        var snapshot = session.Snapshot(TimeSpan.FromMilliseconds(50));
        var stopped = session.Stop();

        var expected = stopped.StartedAtUtc!.Value
                       + Stopwatch.GetElapsedTime(stopped.StartTimestamp, snapshot.StartTimestamp);

        Assert.IsTrue(snapshot.StartTimestamp > stopped.StartTimestamp);
        Assert.AreEqual(expected, snapshot.StartedAtUtc);
    }

    [TestMethod]
    public void FromEvents_HasNoAnchor()
    {
        var session = TraceSession.FromEvents([], 0, 0, 1_000_000);

        Assert.IsNull(session.StartedAtUtc);
    }
}
