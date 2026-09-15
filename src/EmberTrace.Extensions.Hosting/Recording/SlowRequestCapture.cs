using EmberTrace.Extensions.Hosting.Configuration;
using EmberTrace.Sessions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EmberTrace.Extensions.Hosting.Recording;

internal sealed class SlowRequestCapture(
    IOptionsMonitor<EmberTraceOptions> options,
    ILogger<SlowRequestCapture> logger,
    TimeProvider time)
{
    private long _nextAllowedUtcTicks = long.MinValue;

    public Task? TryCapture(string request, TimeSpan elapsed)
    {
        var current = options.CurrentValue;
        var slow = current.SlowRequests;

        if (!slow.Enabled || !Tracer.IsRunning)
            return null;

        var now = time.GetUtcNow();
        var allowedFrom = Volatile.Read(ref _nextAllowedUtcTicks);

        if (now.UtcTicks < allowedFrom
            || Interlocked.CompareExchange(ref _nextAllowedUtcTicks, (now + slow.Cooldown).UtcTicks, allowedFrom)
            != allowedFrom)
            return null;

        var path = Path.Combine(slow.Directory!,
            DumpFileName.Create(current.Dump.FileNamePrefix, "slow", now, TraceFormat.FileExtension));

        return Task.Run(() => Write(Tracer.Snapshot(slow.Window), path, request, elapsed));
    }

    private void Write(TraceSession session, string path, string request, TimeSpan elapsed)
    {
        if (session.EventCount == 0)
            return;

        try
        {
            TraceFormat.Write(session, path);
            logger.LogWarning("EmberTrace captured {Request} ({Elapsed}) to {Path}.", request, elapsed, path);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "EmberTrace could not write the slow request capture to {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "EmberTrace could not write the slow request capture to {Path}.", path);
        }
    }
}
