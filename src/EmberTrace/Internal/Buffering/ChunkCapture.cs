namespace EmberTrace.Internal.Buffering;

internal readonly struct ChunkCapture(Chunk chunk, long version, int count)
{
    public readonly Chunk Chunk = chunk;
    public readonly long Version = version;
    public readonly int Count = count;
}
