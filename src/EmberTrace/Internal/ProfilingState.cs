using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using EmberTrace.Internal.Buffering;
using EmberTrace.Sessions;
using EmberTrace.Tracing;

namespace EmberTrace.Internal;

internal sealed class ProfilingState
{
    private static long _nextId;

    private readonly ConcurrentDictionary<int, ThreadWriter> _writers = new();

    public long Id { get; } = Interlocked.Increment(ref _nextId);
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

    public IEnumerable<ThreadWriter> Writers => _writers.Values;

    public ThreadWriter GetWriter() => _writers.GetOrAdd(
        Environment.CurrentManagedThreadId,
        static (_, state) => new ThreadWriter(state.Collector, state.Sampling),
        this);
}
