namespace EmberTrace.Internal.Buffering;

internal sealed class Chunk
{
    public readonly TraceEvent[] Events;
    public int Count;

    public Chunk(int capacity)
    {
        Events = new TraceEvent[capacity];
    }

    public bool IsFull => Count >= Events.Length;

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
    }
}