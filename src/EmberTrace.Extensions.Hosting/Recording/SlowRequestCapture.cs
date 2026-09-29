using System.Diagnostics;
using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Sessions;
using Microsoft.Extensions.Logging;

namespace EmberTrace.Extensions.Hosting.Recording;

internal sealed class SlowRequestCapture(ILogger<SlowRequestCapture> logger, TimeProvider time)
{
    private int _busy;
    private long _nextAllowedUtcTicks = long.MinValue;
    private Task _pending = Task.CompletedTask;

    public Task? TryCapture(EmberTraceOptions options, string request, long startTimestamp, TimeSpan elapsed)
    {
        var slow = options.SlowRequests;

        if (!slow.Enabled || !Tracer.IsRunning || Interlocked.Exchange(ref _busy, 1) == 1)
            return null;

        var now = time.GetUtcNow();
        var previous = _nextAllowedUtcTicks;

        if (now.UtcTicks < previous)
        {
            Volatile.Write(ref _busy, 0);
            return null;
        }

        _nextAllowedUtcTicks = (now + slow.Cooldown).UtcTicks;

        var path = Path.Combine(slow.Directory!,
            DumpFileName.Create(options.Dump.FileNamePrefix, "slow", now, TraceFormat.FileExtension));
        var window = slow.Window;

        var capture = Task.Run(() => Capture(window, startTimestamp, path, request, elapsed, previous));
        Volatile.Write(ref _pending, capture);
        return capture;
    }

    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        await Volatile.Read(ref _pending)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    private void Capture(TimeSpan window, long startTimestamp, string path, string request, TimeSpan elapsed,
        long previous)
    {
        var written = false;

        try
        {
            var session = Tracer.Snapshot(WindowFor(window, startTimestamp));
            if (session.EventCount > 0)
            {
                TraceFormat.Write(session, path);
                written = true;
                logger.LogWarning("EmberTrace captured {Request} ({Elapsed}) to {Path}.", request, elapsed, path);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "EmberTrace could not write the slow request capture to {Path}.", path);
        }
        finally
        {
            if (!written)
                _nextAllowedUtcTicks = previous;

            Volatile.Write(ref _busy, 0);
        }
    }

    private static TimeSpan WindowFor(TimeSpan window, long startTimestamp)
    {
        if (window == TimeSpan.Zero)
            return TimeSpan.Zero;

        var sinceStart = Stopwatch.GetElapsedTime(startTimestamp);
        return sinceStart > window ? sinceStart : window;
    }
}
