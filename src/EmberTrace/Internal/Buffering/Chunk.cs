using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal sealed class Chunk
{
    public readonly TraceEvent[] Events;
    public int Count;
    private long _firstSequence;
    private long[]? _sequences;
    private long _version;

    public Chunk(int capacity)
    {
        Events = new TraceEvent[capacity];
    }

    public Chunk(TraceEvent[] events, int threadId, int trackId, long firstSequence, long[]? sequences = null)
    {
        Events = events;
        Count = events.Length;
        _sequences = sequences;
        Assign(threadId, trackId, firstSequence);
    }

    public int ThreadId { get; private set; }

    public int TrackId { get; private set; }

    public long Version => Volatile.Read(ref _version);

    public bool IsFull => Count >= Events.Length;

    public void Assign(int threadId, int trackId, long firstSequence)
    {
        ThreadId = threadId;
        TrackId = trackId == 0 ? threadId : trackId;
        _firstSequence = firstSequence;
    }

    public long SequenceAt(int index)
    {
        return _sequences is { } sequences ? sequences[index] : _firstSequence + index;
    }

    public TraceEventRecord RecordAt(int index)
    {
        ref readonly var e = ref Events[index];
        return new TraceEventRecord(e.Id, ThreadId, e.Timestamp, e.Kind, e.FlowId, e.Value, SequenceAt(index), TrackId);
    }

    public Chunk Slice(int start, int count)
    {
        var events = new TraceEvent[count];
        Array.Copy(Events, start, events, 0, count);

        var sequences = _sequences;
        if (sequences is null)
            return new Chunk(events, ThreadId, TrackId, _firstSequence + start);

        var copied = new long[count];
        Array.Copy(sequences, start, copied, 0, count);
        return new Chunk(events, ThreadId, TrackId, 0, copied);
    }

    public bool TryWrite(in TraceEvent e)
    {
        var i = Count;
        if ((uint)i >= (uint)Events.Length)
            return false;

        Events[i] = e;
        Volatile.Write(ref Count, i + 1);
        return true;
    }

    public void Reset()
    {
        Count = 0;
        Interlocked.Increment(ref _version);
    }
}
