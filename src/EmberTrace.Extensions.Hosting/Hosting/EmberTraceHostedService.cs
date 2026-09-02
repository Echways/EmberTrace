using System.Globalization;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Extensions.Hosting.Recording;
using EmberTrace.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting;

internal sealed class EmberTraceHostedService : IHostedService
{
    private readonly ILogger<EmberTraceHostedService> _logger;
    private readonly EmberTraceOptions _options;
    private readonly EmberTraceRecorder _recorder;

    public EmberTraceHostedService(
        EmberTraceRecorder recorder,
        IOptions<EmberTraceOptions> options,
        ILogger<EmberTraceHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _recorder.TryStart();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var session = _recorder.TryStop();
        if (session is not null)
            WriteShutdownDump(session);

        return Task.CompletedTask;
    }

    private void WriteShutdownDump(TraceSession session)
    {
        var directory = _options.ShutdownDumpDirectory;
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"{_options.Dump.FileNamePrefix}-shutdown-{stamp}{TraceFormat.FileExtension}");

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
