using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting;

internal sealed class EmberTraceHostedService : IHostedService
{
    private readonly SlowRequestCapture _capture;
    private readonly ILogger<EmberTraceHostedService> _logger;
    private readonly EmberTraceOptions _options;
    private readonly EmberTraceRecorder _recorder;

    public EmberTraceHostedService(
        EmberTraceRecorder recorder,
        SlowRequestCapture capture,
        IOptions<EmberTraceOptions> options,
        ILogger<EmberTraceHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _recorder.TryStart();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _capture.DrainAsync(cancellationToken);

        var session = _recorder.TryStop();
        if (session is not null)
            WriteShutdownDump(session);
    }

    private void WriteShutdownDump(TraceSession session)
    {
        var directory = _options.ShutdownDumpDirectory;
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var path = Path.Combine(directory,
            DumpFileName.Create(_options.Dump.FileNamePrefix, "shutdown", DateTimeOffset.UtcNow, TraceFormat.FileExtension));

        try
        {
            TraceFormat.Write(session, path);
            _logger.LogInformation("EmberTrace shutdown dump written to {Path}.", path);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "EmberTrace could not write the shutdown dump to {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "EmberTrace could not write the shutdown dump to {Path}.", path);
        }
    }
}
