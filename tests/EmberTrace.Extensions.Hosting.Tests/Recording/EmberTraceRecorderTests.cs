using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Tests.Recording;

[TestClass]
[DoNotParallelize]
public sealed class EmberTraceRecorderTests
{
    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();
    }

    private static EmberTraceRecorder Create(EmberTraceOptions options)
    {
        return new EmberTraceRecorder(Options.Create(options), NullLogger<EmberTraceRecorder>.Instance);
    }

    [TestMethod]
    public void TryStart_StartsAndOwnsTheSession()
    {
        var recorder = Create(new EmberTraceOptions());

        Assert.IsTrue(recorder.TryStart());
        Assert.IsTrue(recorder.IsRunning);
        Assert.IsTrue(recorder.OwnsSession);
    }

    [TestMethod]
    public void TryStart_DoesNothingWhenDisabled()
    {
        var recorder = Create(new EmberTraceOptions { Enabled = false });

        Assert.IsFalse(recorder.TryStart());
        Assert.IsFalse(Tracer.IsRunning);
        Assert.IsFalse(recorder.OwnsSession);
    }

    [TestMethod]
    public void TryStart_AttachesToAnAlreadyRunningSession()
    {
        Tracer.Start(new SessionOptions());
        var recorder = Create(new EmberTraceOptions());

        Assert.IsFalse(recorder.TryStart());
        Assert.IsTrue(recorder.IsRunning);
        Assert.IsFalse(recorder.OwnsSession);
    }

    [TestMethod]
    public void TryStop_ReturnsNullWhenItDoesNotOwnTheSession()
    {
        Tracer.Start(new SessionOptions());
        var recorder = Create(new EmberTraceOptions());
        recorder.TryStart();

        Assert.IsNull(recorder.TryStop());
        Assert.IsTrue(Tracer.IsRunning);
    }

    [TestMethod]
    public void TryStop_ReturnsTheSessionItStarted()
    {
        var recorder = Create(new EmberTraceOptions());
        recorder.TryStart();
        Tracer.Instant(Tracer.Id("probe"));

        var session = recorder.TryStop();

        Assert.IsNotNull(session);
        Assert.AreEqual(1L, session.EventCount);
        Assert.IsFalse(Tracer.IsRunning);
        Assert.IsFalse(recorder.OwnsSession);
    }

    [TestMethod]
    public void TryStop_IsIdempotent()
    {
        var recorder = Create(new EmberTraceOptions());
        recorder.TryStart();
        recorder.TryStop();

        Assert.IsNull(recorder.TryStop());
    }

    [TestMethod]
    public void Snapshot_DoesNotStopTheSession()
    {
        var recorder = Create(new EmberTraceOptions());
        recorder.TryStart();
        Tracer.Instant(Tracer.Id("probe"));

        var snapshot = recorder.Snapshot(TimeSpan.Zero);

        Assert.IsTrue(snapshot.IsSnapshot);
        Assert.IsTrue(recorder.IsRunning);
    }

    [TestMethod]
    public void Constructor_RegistersRouteMetadata()
    {
        Create(new EmberTraceOptions());
        var id = HttpTraceIds.Resolve("GET /from-recorder", "HTTP GET", "Http", 1024);

        Assert.IsTrue(EmberTrace.Metadata.TraceMetadata.CreateDefault().TryGet(id, out var meta));
        Assert.AreEqual("Http", meta.Category);
    }
}
