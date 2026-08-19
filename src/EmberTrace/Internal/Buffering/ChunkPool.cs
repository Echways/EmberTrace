using System.Collections.Concurrent;

namespace EmberTrace.Internal.Buffering;

internal sealed class ChunkPool
{
    private readonly ConcurrentQueue<Chunk> _free = new();
    private readonly int _capacity;

    public ChunkPool(int capacity)
    {
        _capacity = capacity;
    }

    public Chunk Rent()
    {
        if (!_free.TryDequeue(out var chunk))
            return new Chunk(_capacity);

        chunk.Reset();
        return chunk;
    }

    public void Return(Chunk chunk)
    {
        chunk.Reset();
        _free.Enqueue(chunk);
    }
}
