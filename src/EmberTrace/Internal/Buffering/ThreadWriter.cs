using System.Threading;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal sealed class ThreadWriter
{
    private SessionCollector? _collector;

    private Chunk? _chunk;
    private int _closed;

    public ThreadWriter(SessionCollector collector)
    {
        _collector = collector;
        _chunk = collector.TryRentChunk(out var chunk) ? chunk : null;
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

        if (chunk is null || chunk.IsFull)
        {
            if (chunk is not null)
                collector.MarkChunkInactive(chunk);

            if (!collector.TryRentChunk(out chunk))
            {
                collector.RecordDroppedEvent();
                return;
            }

            _chunk = chunk;
        }

        if (!collector.TryAcceptEvent())
            return;

        var e = new TraceEvent(id, Environment.CurrentManagedThreadId, Timestamp.Now(), kind, flowId, value);

        if (chunk is null)
            return;

        chunk.TryWrite(e);
    }
}
