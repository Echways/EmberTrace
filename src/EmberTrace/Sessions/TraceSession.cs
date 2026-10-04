using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;

namespace EmberTrace.Sessions;

public sealed class TraceSession
{
    private readonly IReadOnlyList<Chunk> _chunks;
    private readonly ITraceMetadataProvider? _metadata;

    internal TraceSession(
        IReadOnlyList<Chunk> chunks,
        long startTimestamp,
        long endTimestamp,
        SessionOptions options,
        IReadOnlyDictionary<int, string> threadNames,
        long droppedEvents,
        long droppedChunks,
        long sampledOutEvents,
        bool wasOverflow,
        ITraceMetadataProvider? metadata = null,
        long timestampFrequency = 0,
        bool isSnapshot = false,
        DateTimeOffset? startedAtUtc = null)
    {
        _chunks = chunks;
        _metadata = metadata;
        StartTimestamp = startTimestamp;
        EndTimestamp = endTimestamp;
        Options = options;
        ThreadNames = threadNames;
        DroppedEvents = droppedEvents;
        DroppedChunks = droppedChunks;
        SampledOutEvents = sampledOutEvents;
        WasOverflow = wasOverflow;
        IsSnapshot = isSnapshot;
        StartedAtUtc = startedAtUtc;
        TimestampFrequency = timestampFrequency > 0 ? timestampFrequency : Timestamp.Frequency;
    }

    public static TraceSession FromEvents(
        IEnumerable<TraceEventRecord> events,
        long startTimestamp,
        long endTimestamp,
        long timestampFrequency = 0,
        IReadOnlyDictionary<int, string>? threadNames = null,
        ITraceMetadataProvider? metadata = null,
        long droppedEvents = 0,
        long droppedChunks = 0,
        long sampledOutEvents = 0,
        bool wasOverflow = false,
        SessionOptions? options = null,
        bool isSnapshot = false,
        DateTimeOffset? startedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(events);

        var sessionOptions = options ?? new SessionOptions();
        var capacity = Math.Max(1024, sessionOptions.ChunkCapacity);

        var chunks = new List<Chunk>();
        var runs = new Dictionary<int, ChunkRun>();

        foreach (var e in events)
        {
            if (!runs.TryGetValue(e.TrackId, out var run))
            {
                run = new ChunkRun(e.ThreadId, e.TrackId);
                runs.Add(e.TrackId, run);
            }

            if (!run.Accepts(e, capacity))
            {
                chunks.Add(run.Seal());
                run = new ChunkRun(e.ThreadId, e.TrackId);
                runs[e.TrackId] = run;
            }

            run.Add(e);
        }

        foreach (var run in runs.Values)
            chunks.Add(run.Seal());

        return new TraceSession(
            chunks,
            startTimestamp,
            endTimestamp,
            sessionOptions,
            threadNames ?? new Dictionary<int, string>(),
            droppedEvents,
            droppedChunks,
            sampledOutEvents,
            wasOverflow,
            metadata,
            timestampFrequency,
            isSnapshot,
            startedAtUtc);
    }

    public long StartTimestamp { get; }
    public DateTimeOffset? StartedAtUtc { get; }
    public long EndTimestamp { get; }
    public SessionOptions Options { get; }
    public IReadOnlyDictionary<int, string> ThreadNames { get; }
    public long DroppedEvents { get; }
    public long DroppedChunks { get; }
    public long SampledOutEvents { get; }
    public bool WasOverflow { get; }
    public bool IsSnapshot { get; }

    public ITraceMetadataProvider Metadata => _metadata ?? TraceMetadata.CreateDefault();

    public long TimestampFrequency { get; }

    public double DurationMs => (EndTimestamp - StartTimestamp) * 1000.0 / TimestampFrequency;

    public long EventCount
    {
        get
        {
            long total = 0;
            for (var i = 0; i < _chunks.Count; i++)
                total += _chunks[i].Count;
            return total;
        }
    }

    public TraceEventEnumerable EnumerateEvents()
    {
        return new TraceEventEnumerable(_chunks);
    }

    public SortedTraceEventEnumerable EnumerateEventsSorted()
    {
        return new SortedTraceEventEnumerable(_chunks);
    }

    public readonly struct TraceEventEnumerable
    {
        private readonly IReadOnlyList<Chunk> _chunks;

        internal TraceEventEnumerable(IReadOnlyList<Chunk> chunks)
        {
            _chunks = chunks;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_chunks);
        }

        public struct Enumerator
        {
            private readonly IReadOnlyList<Chunk> _chunks;
            private int _chunkIndex;
            private int _eventIndex;
            private Chunk? _chunk;

            internal Enumerator(IReadOnlyList<Chunk> chunks)
            {
                _chunks = chunks;
                _chunkIndex = -1;
                _eventIndex = -1;
                _chunk = null;
                Current = default;
            }

            public TraceEventRecord Current { get; private set; }

            public bool MoveNext()
            {
                while (true)
                {
                    if (_chunk is null)
                    {
                        _chunkIndex++;
                        if (_chunkIndex >= _chunks.Count)
                            return false;

                        _chunk = _chunks[_chunkIndex];
                        _eventIndex = -1;
                    }

                    if (++_eventIndex >= _chunk.Count)
                    {
                        _chunk = null;
                        continue;
                    }

                    Current = _chunk.RecordAt(_eventIndex);
                    return true;
                }
            }
        }
    }

    public readonly struct SortedTraceEventEnumerable
    {
        private readonly IReadOnlyList<Chunk> _chunks;

        internal SortedTraceEventEnumerable(IReadOnlyList<Chunk> chunks)
        {
            _chunks = chunks;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_chunks);
        }

        public struct Enumerator
        {
            private readonly PriorityQueue<Cursor, EventKey> _queue;

            internal Enumerator(IReadOnlyList<Chunk> chunks)
            {
                _queue = new PriorityQueue<Cursor, EventKey>(chunks.Count);
                Current = default;

                for (var i = 0; i < chunks.Count; i++)
                    if (chunks[i].Count > 0)
                        Enqueue(chunks[i], 0);
            }

            public TraceEventRecord Current { get; private set; }

            public bool MoveNext()
            {
                if (!_queue.TryDequeue(out var cursor, out _))
                    return false;

                Current = cursor.Chunk.RecordAt(cursor.Index);

                var next = cursor.Index + 1;
                if (next < cursor.Chunk.Count)
                    Enqueue(cursor.Chunk, next);

                return true;
            }

            private readonly void Enqueue(Chunk chunk, int index)
            {
                _queue.Enqueue(
                    new Cursor(chunk, index),
                    new EventKey(chunk.Events[index].Timestamp, chunk.TrackId, chunk.SequenceAt(index)));
            }
        }

        private readonly record struct Cursor(Chunk Chunk, int Index);

        private readonly record struct EventKey(long Timestamp, int TrackId, long Sequence) : IComparable<EventKey>
        {
            public int CompareTo(EventKey other)
            {
                var c = Timestamp.CompareTo(other.Timestamp);
                if (c != 0) return c;
                c = TrackId.CompareTo(other.TrackId);
                return c != 0 ? c : Sequence.CompareTo(other.Sequence);
            }
        }
    }

    private sealed class ChunkRun(int threadId, int trackId)
    {
        private readonly List<TraceEvent> _events = [];
        private readonly List<long> _sequences = [];
        private bool _consecutive = true;
        private long _lastTimestamp = long.MinValue;

        public bool Accepts(in TraceEventRecord e, int capacity)
        {
            return _events.Count < capacity && e.ThreadId == threadId && e.Timestamp >= _lastTimestamp;
        }

        public void Add(in TraceEventRecord e)
        {
            if (_sequences.Count > 0 && e.Sequence != _sequences[^1] + 1)
                _consecutive = false;

            _events.Add(new TraceEvent(e.Id, e.Timestamp, e.Kind, e.FlowId, e.Value));
            _sequences.Add(e.Sequence);
            _lastTimestamp = e.Timestamp;
        }

        public Chunk Seal()
        {
            return _consecutive
                ? new Chunk(_events.ToArray(), threadId, trackId, _sequences[0])
                : new Chunk(_events.ToArray(), threadId, trackId, 0, _sequences.ToArray());
        }
    }
}