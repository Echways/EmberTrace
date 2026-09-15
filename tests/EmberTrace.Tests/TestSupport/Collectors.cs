using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;

namespace EmberTrace.Tests;

internal static class Collectors
{
    public static SessionCollector Create(
        OverflowPolicy policy = OverflowPolicy.DropNew,
        long maxEvents = 0,
        int maxChunks = 0,
        int capacity = 16,
        TimeSpan retention = default,
        Action<OverflowInfo>? onOverflow = null,
        Func<long>? clock = null)
    {
        var options = new SessionOptions
        {
            ChunkCapacity = capacity,
            MaxTotalEvents = maxEvents,
            MaxTotalChunks = maxChunks,
            MaxRetentionWindow = retention,
            OverflowPolicy = policy,
            OnOverflow = onOverflow
        };

        return new SessionCollector(options, new ChunkPool(capacity), capacity, clock);
    }

    public static TraceEvent Event(int id = 7, long timestamp = 100)
    {
        return new TraceEvent(id, 1, timestamp, TraceEventKind.Instant, 0, 0);
    }
}
