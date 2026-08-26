using EmberTrace.Sessions;

namespace EmberTrace.Tests.Tracing;

[TestClass]
[DoNotParallelize]
public class TracerSnapshotTests
{
    [TestMethod]
    public void Snapshot_OnTheDefaultTracer_ReturnsRecordedEvents()
    {
        var id = Tracer.Id("Snapshot.Default");

        Tracer.Start(new SessionOptions { ChunkCapacity = 1024 });

        try
        {
            for (var i = 0; i < 25; i++)
                Tracer.Instant(id);

            var snapshot = Tracer.Snapshot();

            Assert.IsTrue(Tracer.IsRunning);
            Assert.IsTrue(snapshot.IsSnapshot);
            Assert.AreEqual(25L, snapshot.EventCount);
        }
        finally
        {
            Tracer.Stop();
        }
    }
}
