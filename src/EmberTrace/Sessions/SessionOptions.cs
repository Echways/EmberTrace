using System;
using System.Collections.Generic;

namespace EmberTrace.Sessions;

public sealed class SessionOptions
{
    public int ChunkCapacity { get; init; } = 16_384;
    public long MaxTotalEvents { get; init; } = 0;
    public int MaxTotalChunks { get; init; } = 0;
    public OverflowPolicy OverflowPolicy { get; init; } = OverflowPolicy.DropNew;
    public bool EnableRuntimeMetadata { get; init; } = false;
    public int[]? EnabledCategoryIds { get; init; }
    public int[]? DisabledCategoryIds { get; init; }
    public int SampleEveryNGlobal { get; init; } = 0;
    public IReadOnlyDictionary<int, int>? SampleEveryNById { get; init; }
    public int MaxEventsPerSecond { get; init; } = 0;
    public Action<OverflowInfo>? OnOverflow { get; init; }
    public Action<MismatchedEndInfo>? OnMismatchedEnd { get; init; }
}
