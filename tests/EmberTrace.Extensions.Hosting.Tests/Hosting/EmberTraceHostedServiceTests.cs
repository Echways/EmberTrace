using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Hosting;
using EmberTrace.Extensions.Hosting.Recording;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Tests.Hosting;

[TestClass]
[DoNotParallelize]
public sealed class EmberTraceHostedServiceTests
{
    private string _directory = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), "embertrace-hosted-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Tracer.IsRunning)
            Tracer.Stop();

        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    private static EmberTraceHostedService Create(EmberTraceOptions options)
    {
        var wrapped = Options.Create(options);
        var recorder = new EmberTraceRecorder(wrapped, NullLogger<EmberTraceRecorder>.Instance);
        return new EmberTraceHostedService(recorder, wrapped, NullLogger<EmberTraceHostedService>.Instance);
    }

    [TestMethod]
    public async Task StartAsync_StartsTheSession()
    {
        var service = Create(new EmberTraceOptions());

        await service.StartAsync(CancellationToken.None);

        Assert.IsTrue(Tracer.IsRunning);

        await service.StopAsync(CancellationToken.None);
    }

    [TestMethod]
    public async Task StopAsync_StopsTheSession()
    {
        var service = Create(new EmberTraceOptions());
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.IsFalse(Tracer.IsRunning);
    }

    [TestMethod]
    public async Task StopAsync_WritesTheShutdownDump()
    {
        var service = Create(new EmberTraceOptions { ShutdownDumpDirectory = _directory });
        await service.StartAsync(CancellationToken.None);
        Tracer.Instant(Tracer.Id("shutdown-probe"));

        await service.StopAsync(CancellationToken.None);

        var files = Directory.GetFiles(_directory, "*.ember");
        Assert.AreEqual(1, files.Length);

        var session = TraceFormat.Read(files[0]);
        Assert.AreEqual(1L, session.EventCount);
    }

    [TestMethod]
    public async Task StopAsync_WithoutADirectory_WritesNothing()
    {
        var service = Create(new EmberTraceOptions());
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    public async Task StopAsync_IsSafeWhenStartWasSkipped()
    {
        var service = Create(new EmberTraceOptions { Enabled = false });
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None);

        Assert.IsFalse(Tracer.IsRunning);
    }
}
