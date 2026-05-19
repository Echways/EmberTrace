using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace.Internal;

internal sealed class ProfilingState
{
    public SessionOptions Options { get; }
    public ChunkPool Pool { get; }
    public SessionCollector Collector { get; }
    public CategoryFilter? CategoryFilter { get; }
    public SamplingPolicy Sampling { get; }
    public long StartTs { get; }
    public long EndTs { get; set; }

    public ProfilingState(
        SessionOptions options,
        ChunkPool pool,
        SessionCollector collector,
        CategoryFilter? categoryFilter,
        SamplingPolicy sampling,
        long startTs)
    {
        Options = options;
        Pool = pool;
        Collector = collector;
        CategoryFilter = categoryFilter;
        Sampling = sampling;
        StartTs = startTs;
    }
}
