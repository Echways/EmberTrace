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
        bool isSnapshot = false)
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
        bool isSnapshot = false)
    {
        if (events is null) throw new ArgumentNullException(nameof(events));

        var sessionOptions = options ?? new SessionOptions();
        var capacity = Math.Max(1024, sessionOptions.ChunkCapacity);

        var chunks = new List<Chunk>();
        var current = new Chunk(capacity);
        chunks.Add(current);

        foreach (var e in events)
        {
            if (current.IsFull)
            {
                current = new Chunk(capacity);
                chunks.Add(current);
            }

            current.TryWrite(new TraceEvent(
                e.Id, e.ThreadId, e.Timestamp, e.Kind, e.FlowId, e.Value, e.Sequence, e.TrackId));
        }

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
            isSnapshot);
    }

    public long StartTimestamp { get; }
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
            private TraceEvent _current;

            internal Enumerator(IReadOnlyList<Chunk> chunks)
            {
                _chunks = chunks;
                _chunkIndex = -1;
                _eventIndex = 0;
                _chunk = null;
                _current = default;
            }

            public TraceEventRecord Current => new(
                _current.Id,
                _current.ThreadId,
                _current.Timestamp,
                _current.Kind,
                _current.FlowId,
                _current.Value,
                _current.Sequence,
                _current.TrackId);

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
                        _eventIndex = 0;
                    }

                    if (_eventIndex >= _chunk.Count)
                    {
                        _chunk = null;
                        continue;
                    }

                    _current = _chunk.Events[_eventIndex++];
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
            private TraceEvent _current;

            internal Enumerator(IReadOnlyList<Chunk> chunks)
            {
                _queue = new PriorityQueue<Cursor, EventKey>(chunks.Count);
                _current = default;

                for (var i = 0; i < chunks.Count; i++)
                {
                    var chunk = chunks[i];
                    if (chunk.Count == 0)
                        continue;

                    var ev = chunk.Events[0];
                    _queue.Enqueue(new Cursor(chunk, 0, ev), new EventKey(ev));
                }
            }

            public TraceEventRecord Current => new(
                _current.Id,
                _current.ThreadId,
                _current.Timestamp,
                _current.Kind,
                _current.FlowId,
                _current.Value,
                _current.Sequence,
                _current.TrackId);

            public bool MoveNext()
            {
                if (_queue.Count == 0)
                    return false;

                _queue.TryDequeue(out var cursor, out _);
                _current = cursor.Event;

                var nextIndex = cursor.Index + 1;
                if (nextIndex < cursor.Chunk.Count)
                {
                    var ev = cursor.Chunk.Events[nextIndex];
                    _queue.Enqueue(new Cursor(cursor.Chunk, nextIndex, ev), new EventKey(ev));
                }

                return true;
            }
        }

        private readonly struct Cursor
        {
            public readonly Chunk Chunk;
            public readonly int Index;
            public readonly TraceEvent Event;

            public Cursor(Chunk chunk, int index, in TraceEvent ev)
            {
                Chunk = chunk;
                Index = index;
                Event = ev;
            }
        }

        private readonly struct EventKey : IComparable<EventKey>
        {
            private readonly long _timestamp;
            private readonly int _trackId;
            private readonly long _sequence;

            public EventKey(in TraceEvent ev)
            {
                _timestamp = ev.Timestamp;
                _trackId = ev.TrackId;
                _sequence = ev.Sequence;
            }

            public int CompareTo(EventKey other)
            {
                var c = _timestamp.CompareTo(other._timestamp);
                if (c != 0) return c;
                c = _trackId.CompareTo(other._trackId);
                if (c != 0) return c;
                return _sequence.CompareTo(other._sequence);
            }
        }
    }
}