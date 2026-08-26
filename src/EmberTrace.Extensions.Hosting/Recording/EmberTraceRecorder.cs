using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Recording;

public sealed class EmberTraceRecorder
{
    private readonly ILogger<EmberTraceRecorder> _logger;
    private readonly EmberTraceOptions _options;
    private int _owned;

    public EmberTraceRecorder(IOptions<EmberTraceOptions> options, ILogger<EmberTraceRecorder> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        HttpTraceIds.EnsureRegistered();
    }

    public bool IsRunning => Tracer.IsRunning;

    public bool OwnsSession => Volatile.Read(ref _owned) == 1;

    public bool TryStart()
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("EmberTrace is disabled by configuration.");
            return false;
        }

        if (Tracer.IsRunning)
        {
            _logger.LogWarning("EmberTrace attached to a session started elsewhere and will not manage its lifetime.");
            return false;
        }

        if (Interlocked.Exchange(ref _owned, 1) == 1)
            return false;

        try
        {
            Tracer.Start(SessionOptionsFactory.Create(_options));
        }
        catch (InvalidOperationException)
        {
            Volatile.Write(ref _owned, 0);
            _logger.LogWarning("EmberTrace could not start: a session was started concurrently.");
            return false;
        }

        _logger.LogInformation(
            "EmberTrace session started: retention {Retention}, chunk capacity {ChunkCapacity}, counters {Counters}.",
            _options.MaxRetentionWindow,
            _options.ChunkCapacity,
            _options.RuntimeCounters);

        return true;
    }

    public TraceSession? TryStop()
    {
        if (Interlocked.Exchange(ref _owned, 0) == 0)
            return null;

        var session = Tracer.Stop();

        _logger.LogInformation(
            "EmberTrace session stopped: {EventCount} events, {DroppedEvents} dropped.",
            session.EventCount,
            session.DroppedEvents);

        return session;
    }

    public TraceSession Snapshot(TimeSpan window)
    {
        return Tracer.Snapshot(window);
    }
}
