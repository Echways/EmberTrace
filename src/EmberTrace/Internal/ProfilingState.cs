using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using EmberTrace.Internal.Buffering;
using EmberTrace.Metadata;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace.Internal;

internal sealed class ProfilingState
{
    private static long _nextId;

    private readonly ConcurrentDictionary<long, ThreadWriter> _writers = new();
    private int _nextTrackId;

    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public SessionOptions Options { get; }
    public SessionCollector Collector { get; }
    public ITraceMetadataProvider Metadata { get; }
    public CategoryFilter? CategoryFilter { get; }
    public SamplingPolicy Sampling { get; }
    public long StartTs { get; }
    public long EndTs { get; set; }

    public ProfilingState(
        SessionOptions options,
        SessionCollector collector,
        ITraceMetadataProvider metadata,
        CategoryFilter? categoryFilter,
        SamplingPolicy sampling,
        long startTs)
    {
        Options = options;
        Collector = collector;
        Metadata = metadata;
        CategoryFilter = categoryFilter;
        Sampling = sampling;
        StartTs = startTs;
    }

    public IEnumerable<ThreadWriter> Writers => _writers.Values;

    public ThreadWriter GetWriter() => _writers.GetOrAdd(
        ThreadIdentity.Current,
        static (_, state) => new ThreadWriter(state.Collector, state.Sampling, Interlocked.Increment(ref state._nextTrackId)),
        this);
}
