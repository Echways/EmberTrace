using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EmberTrace.Extensions.Hosting.Tests.Recording;

[TestClass]
[DoNotParallelize]
public sealed class SlowRequestCaptureTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "embertrace-slow-" + Guid.NewGuid().ToString("N"));
        Tracer.Start(new SessionOptions { ChunkCapacity = 1024 });
        Tracer.Instant(Tracer.Id("slow-probe"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private SlowRequestCapture Create(ManualTimeProvider clock, bool enabled = true)
    {
        var options = new EmberTraceOptions
        {
            SlowRequests = new EmberTraceSlowRequestOptions
            {
                Enabled = enabled,
                Directory = _directory,
                Cooldown = TimeSpan.FromMinutes(1)
            }
        };

        return new SlowRequestCapture(new TestOptionsMonitor<EmberTraceOptions>(options),
            NullLogger<SlowRequestCapture>.Instance, clock);
    }

    [TestMethod]
    public async Task Capture_WritesAReadableSnapshot()
    {
        var capture = Create(new ManualTimeProvider(DateTimeOffset.UtcNow));

        await capture.TryCapture("GET /orders/{id}", TimeSpan.FromSeconds(2))!;

        var file = Directory.EnumerateFiles(_directory, "*-slow-*.ember").Single();
        Assert.IsTrue(TraceFormat.Read(file).EventCount > 0);
    }

    [TestMethod]
    public async Task SecondCaptureInsideTheCooldown_IsSkipped_AndAllowedAfterIt()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var capture = Create(clock);

        await capture.TryCapture("GET /a", TimeSpan.FromSeconds(2))!;
        clock.Now += TimeSpan.FromSeconds(30);
        Assert.IsNull(capture.TryCapture("GET /a", TimeSpan.FromSeconds(2)));

        clock.Now += TimeSpan.FromSeconds(31);
        await capture.TryCapture("GET /a", TimeSpan.FromSeconds(2))!;

        Assert.HasCount(2, Directory.EnumerateFiles(_directory, "*.ember").ToList());
    }

    [TestMethod]
    public void DisabledCapture_OrStoppedSession_DoesNothing()
    {
        Assert.IsNull(Create(new ManualTimeProvider(DateTimeOffset.UtcNow), enabled: false)
            .TryCapture("GET /a", TimeSpan.FromSeconds(2)));

        Tracer.Stop();

        Assert.IsNull(Create(new ManualTimeProvider(DateTimeOffset.UtcNow))
            .TryCapture("GET /a", TimeSpan.FromSeconds(2)));
        Assert.IsFalse(Directory.Exists(_directory));
    }
}
