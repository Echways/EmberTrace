using System.Collections.Generic;
using System.Threading;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal readonly struct SamplingPolicy
{
    public readonly int GlobalEveryN;
    public readonly IReadOnlyDictionary<int, int>? EveryNById;
    public readonly int MaxEventsPerSecond;

    public SamplingPolicy(int globalEveryN, IReadOnlyDictionary<int, int>? everyNById, int maxEventsPerSecond)
    {
        GlobalEveryN = globalEveryN;
        EveryNById = everyNById;
        MaxEventsPerSecond = maxEventsPerSecond;
    }

    public bool IsEnabled => GlobalEveryN > 1 || (EveryNById is { Count: > 0 });
    public bool HasRateLimit => MaxEventsPerSecond > 0;
}

internal sealed class ThreadWriter
{
    private SessionCollector? _collector;

    private Chunk? _chunk;
    private int _closed;
    private readonly SamplingPolicy _sampling;
    private long _globalSampleCounter;
    private long _sequence;
    private long _rateWindowStart;
    private int _rateWindowCount;
    private Dictionary<int, long>? _perIdSampleCounters;

    public ThreadWriter(SessionCollector collector, SamplingPolicy sampling)
    {
        _collector = collector;
        _chunk = collector.TryRentChunk(out var chunk) ? chunk : null;
        _sampling = sampling;

        var threadName = Thread.CurrentThread.Name;
        if (!string.IsNullOrWhiteSpace(threadName))
            collector.RegisterThreadName(Environment.CurrentManagedThreadId, threadName);
    }

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public void Close() => Interlocked.Exchange(ref _closed, 1);

    public void CloseAndDetach()
    {
        Interlocked.Exchange(ref _closed, 1);
        _collector = null;
        _chunk = null;
    }

    public void Write(int id, TraceEventKind kind, long flowId, long value)
    {
        var collector = _collector;
        var chunk = _chunk;

        if (IsClosed || collector is null || collector.IsClosed)
            return;

        if (!ShouldSample(id, collector))
            return;

        var now = Timestamp.Now();
        if (!ShouldAcceptRate(now, collector))
            return;

        if (chunk is null || chunk.IsFull)
        {
            if (chunk is not null)
                collector.MarkChunkInactive(chunk);

            if (!collector.TryRentChunk(out chunk))
            {
                collector.RecordDroppedEvent(OverflowReason.MaxTotalChunks);
                return;
            }

            _chunk = chunk;
        }

        if (!collector.TryAcceptEvent())
            return;

        var sequence = ++_sequence;
        var e = new TraceEvent(id, Environment.CurrentManagedThreadId, now, kind, flowId, value, sequence);

        if (chunk is null)
            return;

        chunk.TryWrite(e);
    }

    private bool ShouldSample(int id, SessionCollector collector)
    {
        if (!_sampling.IsEnabled)
            return true;

        if (_sampling.EveryNById is { Count: > 0 } perId && perId.TryGetValue(id, out var everyN) && everyN > 1)
        {
            _perIdSampleCounters ??= new Dictionary<int, long>(perId.Count);

            var next = _perIdSampleCounters.TryGetValue(id, out var current) ? current + 1 : 1;
            _perIdSampleCounters[id] = next;

            if (next % everyN != 1)
            {
                collector.RecordSampledOutEvent();
                return false;
            }

            return true;
        }

        if (_sampling.GlobalEveryN > 1)
        {
            var next = ++_globalSampleCounter;
            if (next % _sampling.GlobalEveryN != 1)
            {
                collector.RecordSampledOutEvent();
                return false;
            }
        }

        return true;
    }

    private bool ShouldAcceptRate(long timestamp, SessionCollector collector)
    {
        if (!_sampling.HasRateLimit)
            return true;

        if (_rateWindowStart == 0)
            _rateWindowStart = timestamp;

        if (timestamp - _rateWindowStart >= Timestamp.Frequency)
        {
            _rateWindowStart = timestamp;
            _rateWindowCount = 0;
        }

        _rateWindowCount++;
        if (_rateWindowCount <= _sampling.MaxEventsPerSecond)
            return true;

        return collector.HandleRateLimitExceeded();
    }
}
