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

    private static Chunk CopyAll(Chunk source, int count)
    {
        var copy = new Chunk(count);
        Array.Copy(source.Events, copy.Events, count);
        copy.Count = count;
        return copy;
    }

    private static Chunk? CopyWindow(Chunk source, int count, long minTimestamp)
    {
        var kept = 0;
        for (var i = 0; i < count; i++)
            if (source.Events[i].Timestamp >= minTimestamp)
                kept++;

        if (kept == 0)
            return null;

        var copy = new Chunk(kept);
        var target = 0;

        for (var i = 0; i < count; i++)
        {
            var e = source.Events[i];
            if (e.Timestamp >= minTimestamp)
                copy.Events[target++] = e;
        }

        copy.Count = kept;
        return copy;
    }
}
