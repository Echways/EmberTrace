using System.Threading;

namespace EmberTrace.Internal.Buffering;

internal sealed class ChunkPool
{
    private Chunk? _head;
    private readonly int _capacity;

    public ChunkPool(int capacity)
    {
        _capacity = capacity;
    }

    public Chunk Rent()
    {
        while (true)
        {
            var h = Volatile.Read(ref _head);
            if (h is null)
                return new Chunk(_capacity);

            var n = h.Next;
            if (Interlocked.CompareExchange(ref _head, n, h) == h)
            {
                h.Next = null;
                h.Reset();
                return h;
            }
        }
    }

    public void Return(Chunk chunk)
    {
        chunk.Reset();
        while (true)
        {
            var h = Volatile.Read(ref _head);
            chunk.Next = h;
            if (Interlocked.CompareExchange(ref _head, chunk, h) == h)
                return;
        }
    }
}
