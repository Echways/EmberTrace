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
        var h = Interlocked.Exchange(ref _head, null);
        if (h is null)
            return new Chunk(_capacity);

        var n = h.Next;
        h.Next = null;
        if (n is not null)
            Interlocked.Exchange(ref _head, n);

        h.Reset();
        return h;
    }

    public void Return(Chunk chunk)
    {
        chunk.Reset();
        var h = Volatile.Read(ref _head);
        chunk.Next = h;
        Interlocked.Exchange(ref _head, chunk);
    }
}
