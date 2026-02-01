namespace EmberTrace.Sessions;

public sealed class SessionOptions
{
    public int ChunkCapacity { get; init; } = 16_384;
    public long MaxTotalEvents { get; init; } = 0;
    public int MaxTotalChunks { get; init; } = 0;
    public OverflowPolicy OverflowPolicy { get; init; } = OverflowPolicy.DropNew;
}
