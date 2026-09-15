using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmberTrace.Extensions.Hosting.Tests.Recording;

[TestClass]
[DoNotParallelize]
public sealed class SlowRequestCaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly ManualTimeProvider _clock = new(Now);
    private string _directory = null!;
    private int _probe;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "embertrace-slow-" + Guid.NewGuid().ToString("N"));
        _probe = Tracer.Id("slow-probe");
        Tracer.Start(new SessionOptions { ChunkCapacity = 1024 });
        Tracer.Instant(_probe);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [TestMethod]
    public async Task Capture_WritesASnapshotNamedAfterThePrefixAndTheMoment()
    {
        await Create().TryCapture("GET /orders/{id}", TimeSpan.FromSeconds(2))!;

        var file = Directory.EnumerateFiles(_directory).Single();

        Assert.AreEqual("svc-slow-20260915-100000-000.ember", Path.GetFileName(file));
        var session = TraceFormat.Read(file);
        Assert.IsTrue(session.IsSnapshot);
        Assert.AreEqual(_probe, session.SortedEvents().Single().Id);
        Assert.IsTrue(Tracer.IsRunning);
    }

    [TestMethod]
    public async Task Capture_IsRateLimitedByTheCooldown()
    {
        var capture = Create();

        await capture.TryCapture("GET /a", TimeSpan.FromSeconds(2))!;

        _clock.Now = Now + TimeSpan.FromSeconds(59);
        Assert.IsNull(capture.TryCapture("GET /a", TimeSpan.FromSeconds(2)));

        _clock.Now = Now + TimeSpan.FromSeconds(60);
        await capture.TryCapture("GET /b", TimeSpan.FromSeconds(2))!;

        Assert.HasCount(2, Directory.EnumerateFiles(_directory).ToList());
    }

    [TestMethod]
    public async Task Capture_OfAnEmptySnapshot_WritesNothing()
    {
        Tracer.Stop();
        Tracer.Start(new SessionOptions { ChunkCapacity = 1024 });

        await Create().TryCapture("GET /a", TimeSpan.FromSeconds(2))!;

        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void Capture_WhenDisabledOrWithoutASession_DoesNothing(bool enabled, bool running)
    {
        if (!running)
            Tracer.Stop();

        Assert.IsNull(Create(enabled).TryCapture("GET /a", TimeSpan.FromSeconds(2)));
        Assert.IsFalse(Directory.Exists(_directory));
    }

    private SlowRequestCapture Create(bool enabled = true)
    {
        var options = new EmberTraceOptions
        {
            Dump = { FileNamePrefix = "svc" },
            SlowRequests = { Enabled = enabled, Directory = _directory, Cooldown = TimeSpan.FromMinutes(1) }
        };

        return new SlowRequestCapture(new TestOptionsMonitor<EmberTraceOptions>(options),
            NullLogger<SlowRequestCapture>.Instance, _clock);
    }
}
