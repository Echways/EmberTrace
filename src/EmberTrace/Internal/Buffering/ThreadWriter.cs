using System.Threading;
using EmberTrace.Internal.Time;

namespace EmberTrace.Internal.Buffering;

internal sealed class ThreadWriter
{
    private readonly SessionCollector _collector;
    private readonly ChunkPool _pool;

    private Chunk _chunk;
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

    public void Write(int id, TraceEventKind kind)
    {
        if (IsClosed || _collector.IsClosed)
            return;

        var e = new TraceEvent(id, Environment.CurrentManagedThreadId, Timestamp.Now(), kind);

        if (_chunk.TryWrite(e))
            return;

        _chunk = _pool.Rent();
        _collector.AddChunk(_chunk);
        _chunk.TryWrite(e);
    }
}
