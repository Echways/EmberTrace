using System;
using System.Collections.Generic;
using EmberTrace.Internal.Buffering;

namespace EmberTrace.Sessions;

public sealed class TraceSession
{
    private readonly IReadOnlyList<Chunk> _chunks;

    internal TraceSession(
        IReadOnlyList<Chunk> chunks,
        long startTimestamp,
        long endTimestamp,
        SessionOptions options,
        IReadOnlyDictionary<int, string> threadNames,
        long droppedEvents,
        long droppedChunks,
        long sampledOutEvents,
        bool wasOverflow)
    {
        _chunks = chunks;
        StartTimestamp = startTimestamp;
        EndTimestamp = endTimestamp;
        Options = options;
        ThreadNames = threadNames;
        DroppedEvents = droppedEvents;
        DroppedChunks = droppedChunks;
        SampledOutEvents = sampledOutEvents;
        WasOverflow = wasOverflow;
    }

    public long StartTimestamp { get; }
    public long EndTimestamp { get; }
    public SessionOptions Options { get; }
    public IReadOnlyDictionary<int, string> ThreadNames { get; }
    public long DroppedEvents { get; }
    public long DroppedChunks { get; }
    public long SampledOutEvents { get; }
    public bool WasOverflow { get; }

    public long TimestampFrequency => EmberTrace.Internal.Time.Timestamp.Frequency;

    public double DurationMs => (EndTimestamp - StartTimestamp) * 1000.0 / TimestampFrequency;

    public long EventCount
    {
        get
        {
            long total = 0;
            for (int i = 0; i < _chunks.Count; i++)
                total += _chunks[i].Count;
            return total;
        }
    }

    public TraceEventEnumerable EnumerateEvents() => new(_chunks);
    public SortedTraceEventEnumerable EnumerateEventsSorted() => new(_chunks);

    public readonly struct TraceEventEnumerable
    {
        private readonly IReadOnlyList<Chunk> _chunks;

        internal TraceEventEnumerable(IReadOnlyList<Chunk> chunks)
        {
            _chunks = chunks;
        }

        public Enumerator GetEnumerator() => new(_chunks);

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
                _current.Value);

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

        public Enumerator GetEnumerator() => new(_chunks);

        public struct Enumerator
        {
            private readonly PriorityQueue<Cursor, EventKey> _queue;
            private TraceEvent _current;

            internal Enumerator(IReadOnlyList<Chunk> chunks)
            {
                _queue = new PriorityQueue<Cursor, EventKey>(chunks.Count);
                _current = default;

                for (int i = 0; i < chunks.Count; i++)
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
                _current.Value);

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
            private readonly int _phaseRank;
            private readonly int _threadId;

            public EventKey(in TraceEvent ev)
            {
                _timestamp = ev.Timestamp;
                _phaseRank = PhaseRank(ev.Kind);
                _threadId = ev.ThreadId;
            }

            public int CompareTo(EventKey other)
            {
                var c = _timestamp.CompareTo(other._timestamp);
                if (c != 0) return c;
                c = _phaseRank.CompareTo(other._phaseRank);
                if (c != 0) return c;
                return _threadId.CompareTo(other._threadId);
            }
        }

        private static int PhaseRank(TraceEventKind kind)
        {
            return kind switch
            {
                TraceEventKind.Begin => 0,
                TraceEventKind.FlowStart => 1,
                TraceEventKind.FlowStep => 2,
                TraceEventKind.FlowEnd => 3,
                TraceEventKind.Instant => 4,
                TraceEventKind.Counter => 5,
                TraceEventKind.End => 6,
                _ => 7
            };
        }
    }
}
