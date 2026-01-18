namespace EmberTrace.Public;

public sealed class SessionOptions
{
    public int ChunkCapacity { get; init; } = 16_384;
    public OverflowPolicy OverflowPolicy { get; init; } = OverflowPolicy.Drop;
}