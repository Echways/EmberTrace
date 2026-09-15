using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
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

    [TestMethod]
    public async Task StartAndStop_ControlTheTracerSession()
    {
        var service = Create(new EmberTraceOptions());

        await service.StartAsync(CancellationToken.None);
        Assert.IsTrue(Tracer.IsRunning);

        await service.StopAsync(CancellationToken.None);
        Assert.IsFalse(Tracer.IsRunning);
        Assert.IsFalse(Directory.Exists(_directory));
    }

    [TestMethod]
    public async Task StopAsync_WritesAReadableShutdownDump()
    {
        var service = Create(new EmberTraceOptions { ShutdownDumpDirectory = _directory, Dump = { FileNamePrefix = "app" } });
        await service.StartAsync(CancellationToken.None);
        var probe = Tracer.Id("shutdown-probe");
        Tracer.Instant(probe);

        await service.StopAsync(CancellationToken.None);

        var file = Directory.GetFiles(_directory).Single();
        StringAssert.StartsWith(Path.GetFileName(file), "app-shutdown-");
        StringAssert.EndsWith(file, TraceFormat.FileExtension);
        Assert.AreEqual(probe, TraceFormat.Read(file).SortedEvents().Single().Id);
    }

    [TestMethod]
    public async Task StopAsync_WhenTheDumpCannotBeWritten_StillStopsTheSession()
    {
        File.WriteAllText(_directory, "not a directory");
        try
        {
            var service = Create(new EmberTraceOptions { ShutdownDumpDirectory = _directory });
            await service.StartAsync(CancellationToken.None);
            Tracer.Instant(1);

            await service.StopAsync(CancellationToken.None);

            Assert.IsFalse(Tracer.IsRunning);
        }
        finally
        {
            File.Delete(_directory);
        }
    }

    [TestMethod]
    public async Task DisabledRecorder_NeitherStartsNorStops()
    {
        Tracer.Start(new SessionOptions());
        var service = Create(new EmberTraceOptions { Enabled = false, ShutdownDumpDirectory = _directory });

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.IsTrue(Tracer.IsRunning);
        Assert.IsFalse(Directory.Exists(_directory));
    }

    private static EmberTraceHostedService Create(EmberTraceOptions options)
    {
        var wrapped = Options.Create(options);
        var recorder = new EmberTraceRecorder(wrapped, NullLogger<EmberTraceRecorder>.Instance);
        return new EmberTraceHostedService(recorder, wrapped, NullLogger<EmberTraceHostedService>.Instance);
    }
}
