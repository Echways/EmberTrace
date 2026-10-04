using System.Collections.Concurrent;

namespace EmberTrace.Internal.Buffering;

internal sealed class ChunkPool
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<Chunk> _free = new();
    private readonly int _maxRetained;
    private int _retained;

    public ChunkPool(int capacity, int maxRetained = int.MaxValue)
    {
        _capacity = capacity;
        _maxRetained = maxRetained;
    }

    public static int DefaultMaxRetained => Math.Max(16, Environment.ProcessorCount * 2);

    public int Retained => Volatile.Read(ref _retained);

    public Chunk Rent()
    {
        if (!_free.TryDequeue(out var chunk))
            return new Chunk(_capacity);

        Interlocked.Decrement(ref _retained);
        chunk.Reset();
        return chunk;
    }

    public void Return(Chunk chunk)
    {
        if (Interlocked.Increment(ref _retained) > _maxRetained)
        {
            Interlocked.Decrement(ref _retained);
            return;
        }

        chunk.Reset();
        _free.Enqueue(chunk);
    }
}