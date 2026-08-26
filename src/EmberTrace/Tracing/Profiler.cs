using System.Runtime.CompilerServices;
using EmberTrace.Flow;
using EmberTrace.Internal;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Runtime;
using EmberTrace.Internal.Time;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tracing;

internal sealed class Profiler
{
    private static readonly TimeSpan MaxRetentionWindowLimit = TimeSpan.FromDays(1);

    [ThreadStatic] private static long _cachedSessionId;
    [ThreadStatic] private static ThreadWriter? _cachedWriter;
    private int _enabled;
    private ITraceMetadataProvider? _metadata;
    private long _nextFlowId;
    private RuntimeCounterSampler? _runtimeSampler;
    private ProfilingState? _state;

    public bool IsRunning => Volatile.Read(ref _enabled) == 1;

    public ITraceMetadataProvider Metadata => _metadata ?? TraceMetadata.CreateDefault();

    public void Start(SessionOptions? options = null)
    {
        var opts = options ?? new SessionOptions();
        ValidateRetention(opts);

        if (Interlocked.Exchange(ref _enabled, 1) == 1)
            throw new InvalidOperationException("Profiler session already running.");

        var chunkCapacity = Math.Max(1024, opts.ChunkCapacity);
        var collector = new SessionCollector(opts, new ChunkPool(chunkCapacity), chunkCapacity);
        _nextFlowId = 0;

        var meta = TraceMetadata.CreateDefault();
        if (opts.EnableRuntimeMetadata)
            meta = TraceMetadata.Combine(meta, Tracer.Names);

        if (opts.RuntimeCounters != RuntimeCounters.None)
            meta = TraceMetadata.Combine(meta, RuntimeCounterMetadata.Instance);

        CategoryFilter? categoryFilter = null;
        if ((opts.EnabledCategoryIds?.Length ?? 0) > 0 || (opts.DisabledCategoryIds?.Length ?? 0) > 0)
            categoryFilter = new CategoryFilter(meta, opts.EnabledCategoryIds, opts.DisabledCategoryIds);

        var sampling = new SamplingPolicy(opts.SampleEveryNGlobal, opts.SampleEveryNById, opts.MaxEventsPerSecond);

        _metadata = meta;
        _state = new ProfilingState(opts, collector, meta, categoryFilter, sampling, Timestamp.Now());

        if (opts.RuntimeCounters != RuntimeCounters.None)
        {
            var sampler = new RuntimeCounterSampler(this, opts.RuntimeCounters, opts.RuntimeCounterInterval);
            _runtimeSampler = sampler;
            sampler.Start();
        }
    }

    public TraceSession Stop()
    {
        if (Interlocked.Exchange(ref _enabled, 0) == 0)
            throw new InvalidOperationException("Profiler session is not running.");

        var sampler = _runtimeSampler;
        _runtimeSampler = null;
        sampler?.Dispose();

        var state = _state;
        _state = null;

        if (state is null)
            return EmptySession(false);

        state.EndTs = Timestamp.Now();

        var collector = state.Collector;
        collector.Close();

        foreach (var writer in state.Writers)
            writer.DrainAndDetach();

        var chunks = CopySnapshot(collector, 0);

        return new TraceSession(
            chunks,
            state.StartTs,
            state.EndTs,
            state.Options,
            collector.ThreadNames,
            collector.DroppedEvents,
            collector.DroppedChunks,
            collector.SampledOutEvents,
            collector.WasOverflow,
            state.Metadata);
    }

    public TraceSession Snapshot(TimeSpan window)
    {
        if (window < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), window, "The snapshot window cannot be negative.");

        var state = _state;
        if (state is null || !IsRunning)
            return EmptySession(true);

        var collector = state.Collector;
        var captures = collector.BeginSnapshot();

        try
        {
            var cut = Timestamp.Now();
            var min = WindowStart(cut, window);
            var chunks = SnapshotBuilder.Copy(captures, min, out var discarded);
            collector.RecordSnapshotDiscard(discarded);

            return new TraceSession(
                chunks,
                min > 0 ? Math.Max(state.StartTs, min) : state.StartTs,
                cut,
                state.Options,
                collector.ThreadNames,
                collector.DroppedEvents,
                collector.DroppedChunks,
                collector.SampledOutEvents,
                collector.WasOverflow,
                state.Metadata,
                0,
                true);
        }
        finally
        {
            collector.EndSnapshot();
        }
    }

    private static Chunk[] CopySnapshot(SessionCollector collector, long minTimestamp)
    {
        var captures = collector.BeginSnapshot();

        try
        {
            var chunks = SnapshotBuilder.Copy(captures, minTimestamp, out var discarded);
            collector.RecordSnapshotDiscard(discarded);
            return chunks;
        }
        finally
        {
            collector.EndSnapshot();
        }
    }

    private static void ValidateRetention(SessionOptions options)
    {
        if (options.MaxRetentionWindow <= TimeSpan.Zero)
            return;

        if (options.MaxRetentionWindow > MaxRetentionWindowLimit)
            throw new ArgumentOutOfRangeException(
                nameof(SessionOptions.MaxRetentionWindow),
                options.MaxRetentionWindow,
                "MaxRetentionWindow must not exceed one day.");

        if (options.OverflowPolicy != OverflowPolicy.DropOldest)
            throw new ArgumentException(
                "MaxRetentionWindow requires OverflowPolicy.DropOldest.",
                nameof(options));
    }

    private static long WindowStart(long cut, TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
            return 0;

        var seconds = window.TotalSeconds;
        if (seconds >= long.MaxValue / (double)Timestamp.Frequency)
            return 0;

        var min = cut - (long)(seconds * Timestamp.Frequency);
        return min > 0 ? min : 0;
    }

    private TraceSession EmptySession(bool isSnapshot)
    {
        return new TraceSession(
            Array.Empty<Chunk>(),
            0,
            0,
            new SessionOptions(),
            new Dictionary<int, string>(),
            0,
            0,
            0,
            false,
            Metadata,
            0,
            isSnapshot);
    }

    public Scope Scope(int id)
    {
        if (!IsRunning) return new Scope(id, null, false);
        Write(id, TraceEventKind.Begin, 0, AsyncScopeContext.Current);
        return new Scope(id, this, true);
    }

    internal void EndScope(int id)
    {
        EndImpl(id);
    }

    private void EndImpl(int id)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.End, 0, AsyncScopeContext.Current);
    }

    internal void BeginAsyncScope(int id, long scopeId, long parentScopeId)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.Begin, scopeId, parentScopeId);
    }

    internal void EndAsyncScope(int id, long scopeId, long parentScopeId)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.End, scopeId, parentScopeId);
    }

    public long NewFlowId()
    {
        var x = Interlocked.Increment(ref _nextFlowId);
        return x == 0 ? Interlocked.Increment(ref _nextFlowId) : x;
    }

    public void FlowStart(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowStart, flowId, 0);
    }

    public void FlowStep(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowStep, flowId, 0);
    }

    public void FlowEnd(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowEnd, flowId, 0);
    }

    public long FlowStartNew(int id)
    {
        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return flowId;
    }

    public void Instant(int id)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.Instant, 0, 0);
    }

    public void Counter(int id, long value)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.Counter, 0, value);
    }

    public FlowScope Flow(int id)
    {
        if (!IsRunning)
            return new FlowScope(id, 0, false, this);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowScope(id, flowId, true, this);
    }

    public FlowHandle FlowStartNewHandle(int id)
    {
        if (!IsRunning)
            return new FlowHandle(id, 0, this);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowHandle(id, flowId, this);
    }

    private void Write(int id, TraceEventKind kind, long flowId, long value)
    {
        var state = _state;
        if (state is null || state.Collector.IsClosed)
            return;

        var filter = state.CategoryFilter;
        if (filter is not null && !filter.Allows(id))
            return;

        var writer = _cachedWriter;
        if (writer is null || _cachedSessionId != state.Id)
            writer = AcquireWriter(state);

        writer.Write(id, kind, flowId, value);
    }

    internal void WriteRuntime(int id, TraceEventKind kind, long value, long timestamp)
    {
        var state = _state;
        if (state is null || state.Collector.IsClosed)
            return;

        state.GetWriter().WriteAt(id, kind, 0, value, timestamp);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ThreadWriter AcquireWriter(ProfilingState state)
    {
        var writer = state.GetWriter();
        _cachedWriter = writer;
        _cachedSessionId = state.Id;
        return writer;
    }
}