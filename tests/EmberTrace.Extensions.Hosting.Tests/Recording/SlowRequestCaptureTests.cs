using System.Diagnostics;
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
    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(2);

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
        await Create().TryCapture(Slow(), "GET /orders/{id}", Stopwatch.GetTimestamp(), Elapsed)!;

        var file = Directory.EnumerateFiles(_directory).Single();

        Assert.AreEqual("svc-slow-20260915-100000-000.ember", Path.GetFileName(file));
        var session = TraceFormat.Read(file);
        Assert.IsTrue(session.IsSnapshot);
        Assert.AreEqual(_probe, session.SortedEvents().Single().Id);
        Assert.IsTrue(Tracer.IsRunning);
    }

    [TestMethod]
    public async Task Capture_KeepsTheWholeRequestWhenItOutlivesTheWindow()
    {
        var request = Tracer.Id("slow-request");
        var started = Stopwatch.GetTimestamp();
        using (Tracer.Scope(request))
            Thread.Sleep(200);

        await Create().TryCapture(Slow(window: TimeSpan.FromMilliseconds(50)), "GET /a", started,
            Stopwatch.GetElapsedTime(started))!;

        var events = TraceFormat.Read(Directory.EnumerateFiles(_directory).Single()).SortedEvents();

        CollectionAssert.AreEqual(
            new[] { TraceEventKind.Begin, TraceEventKind.End },
            events.Where(e => e.Id == request).Select(e => e.Kind).ToArray());
    }

    [TestMethod]
    public async Task Capture_IsRateLimitedByTheCooldown()
    {
        var capture = Create();

        await capture.TryCapture(Slow(), "GET /a", Stopwatch.GetTimestamp(), Elapsed)!;

        _clock.Now = Now + TimeSpan.FromSeconds(59);
        Assert.IsNull(capture.TryCapture(Slow(), "GET /a", Stopwatch.GetTimestamp(), Elapsed));

        _clock.Now = Now + TimeSpan.FromSeconds(60);
        await capture.TryCapture(Slow(), "GET /b", Stopwatch.GetTimestamp(), Elapsed)!;

        Assert.HasCount(2, Directory.EnumerateFiles(_directory).ToList());
    }

    [TestMethod]
    public async Task Capture_OfAnEmptySnapshot_WritesNothingAndKeepsTheCooldownFree()
    {
        Tracer.Stop();
        Tracer.Start(new SessionOptions { ChunkCapacity = 1024 });
        var capture = Create();

        await capture.TryCapture(Slow(), "GET /a", Stopwatch.GetTimestamp(), Elapsed)!;
        Assert.IsFalse(Directory.Exists(_directory));

        Tracer.Instant(_probe);
        await capture.TryCapture(Slow(), "GET /a", Stopwatch.GetTimestamp(), Elapsed)!;

        Assert.HasCount(1, Directory.EnumerateFiles(_directory).ToList());
    }

    [TestMethod]
    public async Task FailedCapture_NeitherFaultsNorStartsTheCooldown()
    {
        var capture = Create();

        await capture.TryCapture(Slow(directory: "bad\0dir"), "GET /a", Stopwatch.GetTimestamp(), Elapsed)!;
        await capture.TryCapture(Slow(), "GET /a", Stopwatch.GetTimestamp(), Elapsed)!;

        Assert.HasCount(1, Directory.EnumerateFiles(_directory).ToList());
    }

    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, false)]
    public void Capture_WhenDisabledOrWithoutASession_DoesNothing(bool enabled, bool running)
    {
        if (!running)
            Tracer.Stop();

        Assert.IsNull(Create().TryCapture(Slow(enabled), "GET /a", Stopwatch.GetTimestamp(), Elapsed));
        Assert.IsFalse(Directory.Exists(_directory));
    }

    private SlowRequestCapture Create()
    {
        return new SlowRequestCapture(NullLogger<SlowRequestCapture>.Instance, _clock);
    }

    private EmberTraceOptions Slow(bool enabled = true, TimeSpan? window = null, string? directory = null)
    {
        return new EmberTraceOptions
        {
            Dump = { FileNamePrefix = "svc" },
            SlowRequests =
            {
                Enabled = enabled,
                Directory = directory ?? _directory,
                Cooldown = TimeSpan.FromMinutes(1),
                Window = window ?? TimeSpan.FromSeconds(10)
            }
        };
    }
}
