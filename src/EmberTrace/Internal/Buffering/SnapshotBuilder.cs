namespace EmberTrace.Internal.Buffering;

internal static class SnapshotBuilder
{
    public static Chunk[] Copy(ChunkCapture[] captures, long minTimestamp, out int discardedChunks)
    {
        discardedChunks = 0;

        if (captures.Length == 0)
            return Array.Empty<Chunk>();

        var result = new List<Chunk>(captures.Length);

        foreach (var capture in captures)
        {
            var count = capture.Count;
            if (count <= 0)
                continue;

            var source = capture.Chunk;
            var copy = minTimestamp > 0 ? CopyWindow(source, count, minTimestamp) : CopyAll(source, count);

            if (source.Version != capture.Version)
            {
                discardedChunks++;
                continue;
            }

            if (copy is not null)
                result.Add(copy);
        }

        return result.ToArray();
    }

    public static Chunk[] HandOver(IReadOnlyList<Chunk> chunks)
    {
        var result = new List<Chunk>(chunks.Count);

        foreach (var chunk in chunks)
        {
            var count = Volatile.Read(ref chunk.Count);
            if (count == 0)
                continue;

            result.Add(count == chunk.Events.Length ? chunk : CopyAll(chunk, count));
        }

        return result.ToArray();
    }

    private static Chunk CopyAll(Chunk source, int count)
    {
        return source.Slice(0, count);
    }

    private static Chunk? CopyWindow(Chunk source, int count, long minTimestamp)
    {
        var first = FirstAtOrAfter(source.Events, count, minTimestamp);
        return first == count ? null : source.Slice(first, count - first);
    }

    private static int FirstAtOrAfter(TraceEvent[] events, int count, long minTimestamp)
    {
        var low = 0;
        var high = count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (events[middle].Timestamp < minTimestamp)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
