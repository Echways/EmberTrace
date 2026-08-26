using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal sealed class SessionCollector
{
    private const int MinQuarantineChunks = 16;

    private readonly HashSet<Chunk> _active = new();
    private readonly List<Chunk> _chunks = new();
    private readonly Func<long> _clock;
    private readonly Queue<Chunk> _inactive = new();
    private readonly int _maxTotalChunks;
    private readonly long _maxTotalEvents;
    private readonly Action<OverflowInfo>? _onOverflow;
    private readonly OverflowPolicy _policy;

    private readonly ChunkPool _pool;
    private readonly List<Chunk> _quarantine = new();
    private readonly long _retentionTicks;
    private readonly object _sync = new();
    private readonly Dictionary<int, string> _threadNames = new();

    private int _closed;
    private long _droppedChunks;
    private long _droppedEvents;
    private int _overflowed;
    private long _sampledOutEvents;
    private long _snapshotDiscardedChunks;
    private int _snapshotsInFlight;
    private int _totalChunks;

    private long _totalEvents;

    public SessionCollector(SessionOptions options, ChunkPool pool, int chunkCapacity, Func<long>? clock = null)
    {
        _pool = pool;
        _clock = clock ?? Timestamp.Now;
        _policy = options.OverflowPolicy;
        _retentionTicks = RetentionTicks(options.MaxRetentionWindow);
        _maxTotalEvents = options.MaxTotalEvents < 0 ? 0 : options.MaxTotalEvents;
        _maxTotalChunks = options.MaxTotalChunks < 0 ? 0 : options.MaxTotalChunks;
        _onOverflow = options.OnOverflow;

        if (_policy == OverflowPolicy.DropOldest && _maxTotalChunks == 0 && _maxTotalEvents > 0)
        {
            var chunks = (_maxTotalEvents + chunkCapacity - 1) / chunkCapacity;
            _maxTotalChunks = chunks > int.MaxValue ? int.MaxValue : (int)Math.Max(1, chunks);
        }
    }

    public bool IsClosed => Volatile.Read(ref _closed) == 1;
    public bool WasOverflow => Volatile.Read(ref _overflowed) == 1;
    public long DroppedEvents => Interlocked.Read(ref _droppedEvents);
    public long DroppedChunks => Interlocked.Read(ref _droppedChunks);
    public long SampledOutEvents => Interlocked.Read(ref _sampledOutEvents);
    public long SnapshotDiscardedChunks => Interlocked.Read(ref _snapshotDiscardedChunks);

    public ChunkCapture[] BeginSnapshot()
    {
        TrimExpired();
        Interlocked.Increment(ref _snapshotsInFlight);

        lock (_sync)
        {
            var captures = new ChunkCapture[_chunks.Count];

            for (var i = 0; i < _chunks.Count; i++)
            {
                var chunk = _chunks[i];
                captures[i] = new ChunkCapture(chunk, chunk.Version, Volatile.Read(ref chunk.Count));
            }

            return captures;
        }
    }

    public void EndSnapshot()
    {
        if (Interlocked.Decrement(ref _snapshotsInFlight) != 0)
            return;

        Chunk[] pending;

        lock (_sync)
        {
            if (_quarantine.Count == 0)
                return;

            pending = _quarantine.ToArray();
            _quarantine.Clear();
        }

        foreach (var chunk in pending)
            _pool.Return(chunk);
    }

    public void RecordSnapshotDiscard(int chunks)
    {
        if (chunks > 0)
            Interlocked.Add(ref _snapshotDiscardedChunks, chunks);
    }

    public IReadOnlyList<Chunk> Chunks
    {
        get
        {
            lock (_sync)
            {
                return _chunks.ToArray();
            }
        }
    }

    public IReadOnlyDictionary<int, string> ThreadNames
    {
        get
        {
            lock (_sync)
            {
                return new Dictionary<int, string>(_threadNames);
            }
        }
    }

    public void Close()
    {
        Interlocked.Exchange(ref _closed, 1);
    }

    public bool TryAcceptEvent()
    {
        if (IsClosed)
            return false;

        if (_maxTotalEvents <= 0)
            return true;

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

        TrimExpired();

        if (_maxTotalChunks > 0 && Volatile.Read(ref _totalChunks) >= _maxTotalChunks)
        {
            if (_policy == OverflowPolicy.DropOldest)
            {
                if (!TryDropOldestChunk(out var dropped) || dropped is null)
                {
                    MarkOverflow(OverflowReason.MaxTotalChunks);
                    return false;
                }

                Recycle(dropped);
                chunk = _pool.Rent();
                RegisterChunk(chunk, false);
                return true;
            }

            if (_policy == OverflowPolicy.StopSession)
            {
                MarkOverflow(OverflowReason.MaxTotalChunks);
                Close();
            }

            return false;
        }

        chunk = _pool.Rent();
        RegisterChunk(chunk, true);
        return true;
    }

    private static long RetentionTicks(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            return 0;

        var ticks = window.TotalSeconds * Timestamp.Frequency;
        return ticks >= long.MaxValue ? 0 : (long)ticks;
    }

    private static long LastTimestamp(Chunk chunk)
    {
        var count = Volatile.Read(ref chunk.Count);
        return count == 0 ? long.MinValue : chunk.Events[count - 1].Timestamp;
    }

    private void TrimExpired()
    {
        if (_retentionTicks <= 0 || IsClosed)
            return;

        var cutoff = _clock() - _retentionTicks;
        List<Chunk>? expired = null;

        lock (_sync)
        {
            while (_inactive.Count > 0)
            {
                var head = _inactive.Peek();

                if (_active.Contains(head))
                {
                    _inactive.Dequeue();
                    continue;
                }

                if (LastTimestamp(head) >= cutoff)
                    break;

                _inactive.Dequeue();
                if (!_chunks.Remove(head))
                    continue;

                ReleaseEvents(head.Count);
                Interlocked.Increment(ref _droppedChunks);
                Interlocked.Decrement(ref _totalChunks);

                expired ??= new List<Chunk>();
                expired.Add(head);
            }
        }

        if (expired is null)
            return;

        foreach (var chunk in expired)
            Recycle(chunk);
    }

    private void Recycle(Chunk chunk)
    {
        if (Volatile.Read(ref _snapshotsInFlight) > 0)
            lock (_sync)
            {
                if (Volatile.Read(ref _snapshotsInFlight) > 0)
                {
                    if (_quarantine.Count < Math.Max(MinQuarantineChunks, Volatile.Read(ref _totalChunks)))
                        _quarantine.Add(chunk);

                    return;
                }
            }

        _pool.Return(chunk);
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

    private void ReleaseEvents(long count)
    {
        if (count <= 0)
            return;

        if (_maxTotalEvents > 0)
            Interlocked.Add(ref _totalEvents, -count);

        Interlocked.Add(ref _droppedEvents, count);
    }

    private bool TryDropOldestForEvents()
    {
        if (_policy != OverflowPolicy.DropOldest)
            return false;

        List<Chunk>? toRecycle = null;

        lock (_sync)
        {
            while (Interlocked.Read(ref _totalEvents) > _maxTotalEvents)
            {
                if (!TryDropOldestChunkLocked(out var dropped) || dropped is null)
                    break;

                ReleaseEvents(dropped.Count);
                Interlocked.Increment(ref _droppedChunks);
                Interlocked.Decrement(ref _totalChunks);

                toRecycle ??= new List<Chunk>();
                toRecycle.Add(dropped);
            }
        }

        if (toRecycle is not null)
        {
            foreach (var chunk in toRecycle)
                Recycle(chunk);

            MarkOverflow(OverflowReason.MaxTotalEvents);
        }

        return toRecycle is not null && Interlocked.Read(ref _totalEvents) <= _maxTotalEvents;
    }

    private bool TryDropOldestChunk(out Chunk? dropped)
    {
        lock (_sync)
        {
            if (!TryDropOldestChunkLocked(out dropped))
                return false;
        }

        if (dropped is not null)
        {
            ReleaseEvents(dropped.Count);
            Interlocked.Increment(ref _droppedChunks);
            MarkOverflow(OverflowReason.MaxTotalChunks);
        }

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

    private void MarkOverflow(OverflowReason reason)
    {
        if (Interlocked.Exchange(ref _overflowed, 1) != 0)
            return;

        var handler = _onOverflow;
        if (handler is null)
            return;

        ThreadPool.UnsafeQueueUserWorkItem(
            new OverflowNotification(handler, new OverflowInfo(reason, _policy)),
            false);
    }

    public void Clear()
    {
        lock (_sync)
        {
            _chunks.Clear();
            _inactive.Clear();
            _active.Clear();
            _quarantine.Clear();
            _threadNames.Clear();
            Volatile.Write(ref _snapshotsInFlight, 0);
            Volatile.Write(ref _snapshotDiscardedChunks, 0L);
            Volatile.Write(ref _closed, 0);
            Volatile.Write(ref _overflowed, 0);
            Volatile.Write(ref _totalEvents, 0L);
            Volatile.Write(ref _totalChunks, 0);
            Volatile.Write(ref _droppedEvents, 0L);
            Volatile.Write(ref _droppedChunks, 0L);
            Volatile.Write(ref _sampledOutEvents, 0L);
        }
    }

    private sealed class OverflowNotification(Action<OverflowInfo> handler, OverflowInfo info) : IThreadPoolWorkItem
    {
        public void Execute()
        {
            try
            {
                handler(info);
            }
            catch
            {
            }
        }
    }
}