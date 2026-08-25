using EmberTrace.Internal;

namespace EmberTrace.Sessions;

public sealed class SessionOptions
{
    public int ChunkCapacity { get; init; } = 16_384;
    public long MaxTotalEvents { get; init; } = 0;
    public int MaxTotalChunks { get; init; } = 0;
    public OverflowPolicy OverflowPolicy { get; init; } = OverflowPolicy.DropNew;

    public bool EnableRuntimeMetadata { get; init; } =
        RuntimeConfig.GetBool("EmberTrace.EnableRuntimeMetadata", false);

    public int[]? EnabledCategoryIds { get; init; }
    public int[]? DisabledCategoryIds { get; init; }
    public int SampleEveryNGlobal { get; init; } = 0;
    public IReadOnlyDictionary<int, int>? SampleEveryNById { get; init; }
    public int MaxEventsPerSecond { get; init; } = 0;
    public RuntimeCounters RuntimeCounters { get; init; } = RuntimeCounters.None;
    public TimeSpan RuntimeCounterInterval { get; init; } = TimeSpan.FromMilliseconds(50);
    public Action<OverflowInfo>? OnOverflow { get; init; }
    public Action<MismatchedEndInfo>? OnMismatchedEnd { get; init; }
}