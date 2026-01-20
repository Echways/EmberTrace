using System.Collections.Generic;
using System.Threading;

namespace EmberTrace.Internal.Buffering;

internal sealed class SessionCollector
{
    private readonly List<Chunk> _chunks = new();
    private readonly List<ThreadWriter> _writers = new();
    private readonly object _sync = new();

    private int _closed;

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public void Close() => Interlocked.Exchange(ref _closed, 1);

    public void AddChunk(Chunk chunk)
    {
        lock (_sync)
            _chunks.Add(chunk);
    }

    public void RegisterWriter(ThreadWriter writer)
    {
        lock (_sync)
            _writers.Add(writer);
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

    public void Clear()
    {
        lock (_sync)
        {
            _chunks.Clear();
            _writers.Clear();
            _closed = 0;
        }
    }
}
