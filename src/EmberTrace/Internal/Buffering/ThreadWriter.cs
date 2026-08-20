using System.Collections.Generic;
using System.Threading;
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
    private SessionCollector? _collector;

    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private Chunk? _chunk;
    private int _writesInFlight;
    private readonly SamplingPolicy _sampling;
    private long _sequence;
    private long _rateWindowStart;
    private int _rateWindowCount;
    private TicketBlock[]? _ticketBlocks;

    public ThreadWriter(SessionCollector collector, SamplingPolicy sampling)
    {
        _collector = collector;
        _chunk = collector.TryRentChunk(out var chunk) ? chunk : null;
        _sampling = sampling;

        var threadName = Thread.CurrentThread.Name;
        if (!string.IsNullOrWhiteSpace(threadName))
            collector.RegisterThreadName(Environment.CurrentManagedThreadId, threadName);
    }

    public void DrainAndDetach()
    {
        if (_ownerThreadId != Environment.CurrentManagedThreadId)
        {
            var spin = new SpinWait();
            while (Volatile.Read(ref _writesInFlight) != 0)
                spin.SpinOnce();
        }

        _collector = null;
        _chunk = null;
    }

    public void Write(int id, TraceEventKind kind, long flowId, long value)
    {
        var collector = _collector;
        if (collector is null)
            return;

        var depth = Interlocked.Increment(ref _writesInFlight);
        try
        {
            if (collector.IsClosed)
                return;

            WriteCore(id, kind, flowId, value, collector);
        }
        finally
        {
            Volatile.Write(ref _writesInFlight, depth - 1);
        }
    }

    private void WriteCore(int id, TraceEventKind kind, long flowId, long value, SessionCollector collector)
    {
        if (!ShouldSample(id, collector))
            return;

        var now = Timestamp.Now();
        if (!ShouldAcceptRate(now, collector))
            return;

        var chunk = _chunk;
        if (chunk is null || chunk.IsFull)
        {
            if (chunk is not null)
                collector.MarkChunkInactive(chunk);

            if (!collector.TryRentChunk(out chunk) || chunk is null)
            {
                collector.RecordDroppedEvent(OverflowReason.MaxTotalChunks);
                return;
            }

            _chunk = chunk;
        }

        if (!collector.TryAcceptEvent())
            return;

        chunk.TryWrite(new TraceEvent(id, Environment.CurrentManagedThreadId, now, kind, flowId, value, ++_sequence));
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
