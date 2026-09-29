using System.Diagnostics;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace.Tests.Sessions;

[TestClass]
public class SessionClockTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

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
    [DataRow(0)]
    [DataRow(50)]
    public void Snapshot_IsAnchoredToTheWallClockAtItsCut(int windowMs)
    {
        var clock = new ManualTimeProvider(Noon);
        var profiler = new Profiler(clock);
        profiler.Start(new SessionOptions { ChunkCapacity = 1024 });
        profiler.Instant(1);
        Thread.Sleep(100);
        clock.Now += TimeSpan.FromMinutes(3);

        var snapshot = profiler.Snapshot(TimeSpan.FromMilliseconds(windowMs));
        profiler.Stop();

        Assert.AreEqual(clock.Now,
            snapshot.StartedAtUtc!.Value + Stopwatch.GetElapsedTime(snapshot.StartTimestamp, snapshot.EndTimestamp));
    }

    [TestMethod]
    public void Stop_KeepsTheAnchorTakenAtStart()
    {
        var clock = new ManualTimeProvider(Noon);
        var profiler = new Profiler(clock);
        profiler.Start(new SessionOptions { ChunkCapacity = 1024 });
        clock.Now += TimeSpan.FromMinutes(3);

        Assert.AreEqual(Noon, profiler.Stop().StartedAtUtc);
    }

    [TestMethod]
    public void FromEvents_HasNoAnchor()
    {
        Assert.IsNull(TraceSession.FromEvents([], 0, 0, 1_000_000).StartedAtUtc);
    }

    [TestMethod]
    public void FromEvents_KeepsTheGivenAnchor()
    {
        Assert.AreEqual(Noon, TraceSession.FromEvents([], 0, 0, 1_000_000, startedAtUtc: Noon).StartedAtUtc);
    }
}
