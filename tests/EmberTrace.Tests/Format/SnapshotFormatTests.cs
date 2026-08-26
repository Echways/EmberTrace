using EmberTrace.Sessions;

namespace EmberTrace.Tests.Format;

[TestClass]
public class SnapshotFormatTests
{
    [TestMethod]
    public void RoundTrip_PreservesTheSnapshotFlag()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });

        for (var i = 0; i < 10; i++)
            session.Instant(11);

        var snapshot = session.Snapshot();
        session.Stop();

        using var stream = new MemoryStream();
        TraceFormat.Write(snapshot, stream);
        stream.Position = 0;

        var restored = TraceFormat.Read(stream);

        Assert.IsTrue(restored.IsSnapshot);
        Assert.AreEqual(snapshot.EventCount, restored.EventCount);
    }

    [TestMethod]
    public void RoundTrip_KeepsStoppedSessionsUnflagged()
    {
        using var session = new TracingSession();
        session.Start(new SessionOptions { ChunkCapacity = 1024 });
        session.Instant(11);
        var stopped = session.Stop();

        using var stream = new MemoryStream();
        TraceFormat.Write(stopped, stream);
        stream.Position = 0;

        var restored = TraceFormat.Read(stream);

        Assert.IsFalse(restored.IsSnapshot);
    }
}
