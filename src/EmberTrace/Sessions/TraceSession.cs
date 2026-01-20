using System;
using System.Collections.Generic;
using EmberTrace.Internal.Buffering;

namespace EmberTrace.Sessions;

public sealed class TraceSession
{
    private readonly IReadOnlyList<Chunk> _chunks;

    internal TraceSession(IReadOnlyList<Chunk> chunks, long startTimestamp, long endTimestamp, SessionOptions options)
    {
        _chunks = chunks;
        StartTimestamp = startTimestamp;
        EndTimestamp = endTimestamp;
        Options = options;
    }

    public long StartTimestamp { get; }
    public long EndTimestamp { get; }
    public SessionOptions Options { get; }

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
                _current.FlowId);

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
}
