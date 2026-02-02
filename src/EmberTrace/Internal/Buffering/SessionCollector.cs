using System;
using System.Collections.Generic;
using System.Threading;
using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal sealed class SessionCollector
{
    private readonly List<Chunk> _chunks = new();
    private readonly List<ThreadWriter> _writers = new();
    private readonly Queue<Chunk> _inactive = new();
    private readonly HashSet<Chunk> _active = new();
    private readonly Dictionary<int, string> _threadNames = new();
    private readonly object _sync = new();

    private readonly ChunkPool _pool;
    private readonly OverflowPolicy _policy;
    private readonly long _maxTotalEvents;
    private readonly int _maxTotalChunks;
    private readonly Action<OverflowInfo>? _onOverflow;

    private long _totalEvents;
    private int _totalChunks;
    private long _droppedEvents;
    private long _droppedChunks;
    private long _sampledOutEvents;

    private int _closed;
    private int _overflowed;

    public bool IsClosed => Volatile.Read(ref _closed) == 1;
    public bool WasOverflow => Volatile.Read(ref _overflowed) == 1;
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);
    public long DroppedChunks => Interlocked.Read(ref _droppedChunks);
    public long SampledOutEvents => Interlocked.Read(ref _sampledOutEvents);

    public void Close() => Interlocked.Exchange(ref _closed, 1);

    public SessionCollector(SessionOptions options, ChunkPool pool, int chunkCapacity)
    {
        _pool = pool;
        _policy = options.OverflowPolicy;
        _maxTotalEvents = options.MaxTotalEvents < 0 ? 0 : options.MaxTotalEvents;
        _maxTotalChunks = options.MaxTotalChunks < 0 ? 0 : options.MaxTotalChunks;
        _onOverflow = options.OnOverflow;

        if (_policy == OverflowPolicy.DropOldest && _maxTotalChunks == 0 && _maxTotalEvents > 0)
        {
            var chunks = (_maxTotalEvents + chunkCapacity - 1) / chunkCapacity;
            _maxTotalChunks = chunks > int.MaxValue ? int.MaxValue : (int)Math.Max(1, chunks);
        }
    }

    public void RegisterWriter(ThreadWriter writer)
    {
        lock (_sync)
            _writers.Add(writer);
    }

    public bool TryAcceptEvent()
    {
        if (IsClosed)
            return false;

        if (_maxTotalEvents <= 0)
        {
            Interlocked.Increment(ref _totalEvents);
            return true;
        }

        var after = Interlocked.Increment(ref _totalEvents);
        if (after <= _maxTotalEvents)
            return true;

        switch (_policy)
        {
            case OverflowPolicy.DropNew:
                Interlocked.Decrement(ref _totalEvents);
                Interlocked.Increment(ref _droppedEvents);
                MarkOverflow(OverflowReason.MaxTotalEvents);
                return false;
            case OverflowPolicy.StopSession:
                Interlocked.Decrement(ref _totalEvents);
                Interlocked.Increment(ref _droppedEvents);
                MarkOverflow(OverflowReason.MaxTotalEvents);
                Close();
                return false;
            case OverflowPolicy.DropOldest:
                if (!TryDropOldestForEvents())
                {
                    Interlocked.Decrement(ref _totalEvents);
                    Interlocked.Increment(ref _droppedEvents);
                    MarkOverflow(OverflowReason.MaxTotalEvents);
                    return false;
                }
                return true;
            default:
                Interlocked.Decrement(ref _totalEvents);
                Interlocked.Increment(ref _droppedEvents);
                MarkOverflow(OverflowReason.MaxTotalEvents);
                return false;
        }
    }

    public void MarkChunkInactive(Chunk chunk)
    {
        lock (_sync)
        {
            if (_active.Remove(chunk))
                _inactive.Enqueue(chunk);
        }
    }

    public void RecordDroppedEvent(OverflowReason reason)
    {
        Interlocked.Increment(ref _droppedEvents);
        MarkOverflow(reason);
    }

    public bool HandleRateLimitExceeded()
    {
        Interlocked.Increment(ref _droppedEvents);
        MarkOverflow(OverflowReason.RateLimit);

        if (_policy == OverflowPolicy.StopSession)
            Close();

        return false;
    }

    public void RecordSampledOutEvent()
    {
        Interlocked.Increment(ref _sampledOutEvents);
    }

    public void RegisterThreadName(int threadId, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_sync)
        {
            if (!_threadNames.ContainsKey(threadId))
                _threadNames.Add(threadId, name);
        }
    }

    public bool TryRentChunk(out Chunk? chunk)
    {
        chunk = null;
        if (IsClosed)
            return false;

        if (_maxTotalChunks > 0 && Volatile.Read(ref _totalChunks) >= _maxTotalChunks)
        {
            if (_policy == OverflowPolicy.DropOldest)
            {
                if (TryDropOldestChunk(out var dropped) && dropped is not null)
                {
                    chunk = dropped;
                    RegisterChunk(chunk, incrementTotalChunks: false);
                    return true;
                }

                MarkOverflow(OverflowReason.MaxTotalChunks);
                return false;
            }

            if (_policy == OverflowPolicy.StopSession)
            {
                MarkOverflow(OverflowReason.MaxTotalChunks);
                Close();
            }

            return false;
        }

        chunk = _pool.Rent();
        RegisterChunk(chunk, incrementTotalChunks: true);
        return true;
    }

    private void RegisterChunk(Chunk chunk, bool incrementTotalChunks)
    {
        lock (_sync)
        {
            _chunks.Add(chunk);
            _active.Add(chunk);
            if (incrementTotalChunks)
                Interlocked.Increment(ref _totalChunks);
        }
    }

    private bool TryDropOldestForEvents()
    {
        if (_policy != OverflowPolicy.DropOldest)
            return false;

        var droppedAny = false;

        lock (_sync)
        {
            while (Interlocked.Read(ref _totalEvents) > _maxTotalEvents)
            {
                if (!TryDropOldestChunkLocked(out var dropped) || dropped is null)
                    break;

                droppedAny = true;
                AccountDroppedChunk(dropped, decrementTotalChunks: true, OverflowReason.MaxTotalEvents);
            }
        }

        return droppedAny && Interlocked.Read(ref _totalEvents) <= _maxTotalEvents;
    }

    private bool TryDropOldestChunk(out Chunk? dropped)
    {
        lock (_sync)
        {
            if (!TryDropOldestChunkLocked(out dropped))
                return false;
        }

        if (dropped is not null)
            AccountDroppedChunk(dropped, decrementTotalChunks: false, OverflowReason.MaxTotalChunks, reuse: true);

        return dropped is not null;
    }

    private bool TryDropOldestChunkLocked(out Chunk? dropped)
    {
        while (_inactive.Count > 0)
        {
            var candidate = _inactive.Dequeue();
            if (_active.Contains(candidate))
                continue;

            if (!_chunks.Remove(candidate))
                continue;

            dropped = candidate;
            return true;
        }

        dropped = null;
        return false;
    }

    private void AccountDroppedChunk(Chunk chunk, bool decrementTotalChunks, OverflowReason reason, bool reuse = false)
    {
        var count = chunk.Count;
        if (count > 0)
        {
            Interlocked.Add(ref _totalEvents, -count);
            Interlocked.Add(ref _droppedEvents, count);
        }

        Interlocked.Increment(ref _droppedChunks);

        if (decrementTotalChunks)
            Interlocked.Decrement(ref _totalChunks);

        chunk.Reset();

        if (reuse)
        {
            MarkOverflow(reason);
            return;
        }

        _pool.Return(chunk);
        MarkOverflow(reason);
    }

    private void MarkOverflow(OverflowReason reason)
    {
        if (Interlocked.Exchange(ref _overflowed, 1) != 0)
            return;

        var handler = _onOverflow;
        if (handler is null)
            return;

        handler(new OverflowInfo(reason, _policy));
    }

    public IReadOnlyList<Chunk> Chunks
    {
        get
        {
            lock (_sync)
                return _chunks.ToArray();
        }
    }

    public IReadOnlyList<ThreadWriter> Writers
    {
        get
        {
            lock (_sync)
                return _writers.ToArray();
        }
    }

    public IReadOnlyDictionary<int, string> ThreadNames
    {
        get
        {
            lock (_sync)
                return new Dictionary<int, string>(_threadNames);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _chunks.Clear();
            _inactive.Clear();
            _active.Clear();
            _writers.Clear();
            _threadNames.Clear();
            _closed = 0;
            _overflowed = 0;
            _totalEvents = 0;
            _totalChunks = 0;
            _droppedEvents = 0;
            _droppedChunks = 0;
            _sampledOutEvents = 0;
        }
    }
}
