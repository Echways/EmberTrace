using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal readonly struct SamplingPolicy
{
    public readonly int GlobalEveryN;
    public readonly int MaxEventsPerSecond;
    public readonly SampleTicketPool? Tickets;

    public SamplingPolicy(int globalEveryN, IReadOnlyDictionary<int, int>? everyNById, int maxEventsPerSecond)
    {
        GlobalEveryN = globalEveryN;
        MaxEventsPerSecond = maxEventsPerSecond;

        var pool = new SampleTicketPool(everyNById);
        Tickets = globalEveryN > 1 || pool.SlotCount > 1 ? pool : null;
    }

    public bool HasRateLimit => MaxEventsPerSecond > 0;
}

internal sealed class ThreadWriter
{
    private readonly Thread _owner = Thread.CurrentThread;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly SamplingPolicy _sampling;
    private readonly int _trackId;
    private Chunk? _chunk;
    private SessionCollector? _collector;
    private long _lastTimestamp;
    private int _rateWindowCount;
    private long _rateWindowStart;
    private long _sequence;
    private TicketBlock[]? _ticketBlocks;

    public ThreadWriter(SessionCollector collector, SamplingPolicy sampling, int trackId, long ownerKey = 0)
    {
        _collector = collector;
        _trackId = trackId;
        OwnerKey = ownerKey;
        collector.RegisterWriter(this);
        _sampling = sampling;
        _chunk = Rent(collector);

        var threadName = Thread.CurrentThread.Name;
        if (!string.IsNullOrWhiteSpace(threadName))
            collector.RegisterThreadName(_ownerThreadId, threadName);
    }

    public long OwnerKey { get; }

    public bool IsOwnerAlive => _owner.IsAlive;

    public Chunk? Abandon()
    {
        var chunk = Volatile.Read(ref _chunk);
        _collector = null;
        _chunk = null;
        return chunk;
    }

    public bool BelongsTo(SessionCollector collector)
    {
        return ReferenceEquals(_collector, collector);
    }

    public void Detach()
    {
        _collector = null;
        _chunk = null;
    }

    public void Write(int id, TraceEventKind kind, long flowId, long value)
    {
        var collector = _collector;
        if (collector is null || collector.IsClosed)
            return;

        WriteCore(id, kind, flowId, value, collector, 0);
    }

    public void WriteAt(int id, TraceEventKind kind, long flowId, long value, long timestamp)
    {
        var collector = _collector;
        if (collector is null || collector.IsClosed)
            return;

        WriteCore(id, kind, flowId, value, collector, timestamp);
    }

    private void WriteCore(
        int id, TraceEventKind kind, long flowId, long value, SessionCollector collector, long timestamp)
    {
        if (!ShouldSample(id, collector))
            return;

        var now = timestamp != 0 ? Math.Max(timestamp, _lastTimestamp) : Timestamp.Now();
        if (!ShouldAcceptRate(now, collector))
            return;

        var chunk = _chunk;
        if (chunk is null || chunk.IsFull)
        {
            if (chunk is not null)
                collector.MarkChunkInactive(chunk);

            chunk = Rent(collector);
            if (chunk is null)
            {
                collector.RecordDroppedEvent(OverflowReason.MaxTotalChunks);
                return;
            }

            _chunk = chunk;
        }

        if (!collector.TryAcceptEvent())
            return;

        _lastTimestamp = now;
        if (chunk.TryWrite(new TraceEvent(id, now, kind, flowId, value)))
            _sequence++;
    }

    private Chunk? Rent(SessionCollector collector)
    {
        if (!collector.TryRentChunk(out var chunk) || chunk is null)
            return null;

        chunk.Assign(_ownerThreadId, _trackId, _sequence + 1);
        return chunk;
    }

    private bool ShouldSample(int id, SessionCollector collector)
    {
        var tickets = _sampling.Tickets;
        if (tickets is null)
            return true;

        int slot, everyN;
        if (tickets.TryGetSlot(id, out var perId))
        {
            slot = perId.Index;
            everyN = perId.EveryN;
        }
        else if (_sampling.GlobalEveryN > 1)
        {
            slot = SampleTicketPool.GlobalSlot;
            everyN = _sampling.GlobalEveryN;
        }
        else
        {
            return true;
        }

        if (NextTicket(tickets, slot) % everyN == 0)
            return true;

        collector.RecordSampledOutEvent();
        return false;
    }

    private long NextTicket(SampleTicketPool tickets, int slot)
    {
        var blocks = _ticketBlocks ??= new TicketBlock[tickets.SlotCount];
        ref var block = ref blocks[slot];

        if (block.Next == block.End)
        {
            block.Next = tickets.RentBlock(slot);
            block.End = block.Next + SampleTicketPool.BlockSize;
        }

        return block.Next++;
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

    private struct TicketBlock
    {
        public long Next;
        public long End;
    }
}