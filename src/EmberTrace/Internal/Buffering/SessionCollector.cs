using System.Collections.Generic;
using System.Threading;

namespace EmberTrace.Internal.Buffering;

internal sealed class SessionCollector
{
    private readonly List<Chunk> _chunks = new();
    private readonly List<ThreadWriter> _writers = new();

    private int _closed;

    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    public void Close() => Interlocked.Exchange(ref _closed, 1);

    public void AddChunk(Chunk chunk) => _chunks.Add(chunk);

    public void RegisterWriter(ThreadWriter writer) => _writers.Add(writer);

    public IReadOnlyList<Chunk> Chunks => _chunks;

    public IReadOnlyList<ThreadWriter> Writers => _writers;

    public void Clear()
    {
        _chunks.Clear();
        _writers.Clear();
        _closed = 0;
    }
}