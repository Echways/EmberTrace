using System.Threading;
using EmberTrace.Internal.Time;
using EmberTrace.Sessions;

namespace EmberTrace.Internal.Buffering;

internal sealed class ThreadWriter
{
    private SessionCollector? _collector;
    private ChunkPool? _pool;

    private Chunk? _chunk;
    private int _closed;

    public ThreadWriter(SessionCollector collector, ChunkPool pool)
    {
        _collector = collector;
        _pool = pool;

        _chunk = pool.Rent();
        _collector.AddChunk(_chunk);
    }

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public void Close() => Interlocked.Exchange(ref _closed, 1);

    public void CloseAndDetach()
    {
        Interlocked.Exchange(ref _closed, 1);
        _collector = null;
        _pool = null;
        _chunk = null;
    }

    public void Write(int id, TraceEventKind kind, long flowId)
    {
        var collector = _collector;
        var pool = _pool;
        var chunk = _chunk;

        if (IsClosed || collector is null || pool is null || chunk is null || collector.IsClosed)
            return;

        var e = new TraceEvent(id, Environment.CurrentManagedThreadId, Timestamp.Now(), kind, flowId);

        if (chunk.TryWrite(e))
            return;

        chunk = pool.Rent();
        collector.AddChunk(chunk);
        _chunk = chunk;
        chunk.TryWrite(e);
    }
}
