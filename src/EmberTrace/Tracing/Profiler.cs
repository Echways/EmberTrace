using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using EmberTrace.Internal;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tracing;

internal sealed class Profiler
{
    private int _enabled;
    private ProfilingState? _state;
    private long _nextFlowId;

    [ThreadStatic] private static long _cachedSessionId;
    [ThreadStatic] private static ThreadWriter? _cachedWriter;

    public bool IsRunning => Volatile.Read(ref _enabled) == 1;

    public void Start(SessionOptions? options = null)
    {
        if (Interlocked.Exchange(ref _enabled, 1) == 1)
            throw new InvalidOperationException("Profiler session already running.");

        var opts = options ?? new SessionOptions();
#if DEBUG
        Tracer.EnableRuntimeMetadata();
#else
        if (opts.EnableRuntimeMetadata)
            Tracer.EnableRuntimeMetadata();
#endif
        var chunkCapacity = Math.Max(1024, opts.ChunkCapacity);
        var collector = new SessionCollector(opts, new ChunkPool(chunkCapacity), chunkCapacity);
        _nextFlowId = 0;

        var meta = TraceMetadata.CreateDefault();
        CategoryFilter? categoryFilter = null;
        if ((opts.EnabledCategoryIds?.Length ?? 0) > 0 || (opts.DisabledCategoryIds?.Length ?? 0) > 0)
            categoryFilter = new CategoryFilter(meta, opts.EnabledCategoryIds, opts.DisabledCategoryIds);

        var sampling = new SamplingPolicy(opts.SampleEveryNGlobal, opts.SampleEveryNById, opts.MaxEventsPerSecond);

        _state = new ProfilingState(opts, collector, categoryFilter, sampling, Timestamp.Now());
    }

    public TraceSession Stop()
    {
        if (Interlocked.Exchange(ref _enabled, 0) == 0)
            throw new InvalidOperationException("Profiler session is not running.");

        var state = _state;
        _state = null;

        if (state is null)
            return new TraceSession(Array.Empty<Chunk>(), 0, 0, new SessionOptions(), new Dictionary<int, string>(), 0, 0, 0, false);

        state.EndTs = Timestamp.Now();

        var collector = state.Collector;
        collector.Close();

        foreach (var writer in state.Writers)
            writer.DrainAndDetach();

        var chunks = collector.Chunks;
        var snapshot = new Chunk[chunks.Count];
        for (int i = 0; i < chunks.Count; i++)
        {
            var source = chunks[i];
            var copy = new Chunk(source.Count);
            if (source.Count > 0)
                Array.Copy(source.Events, copy.Events, source.Count);
            copy.Count = source.Count;
            snapshot[i] = copy;
        }

        return new TraceSession(
            snapshot,
            state.StartTs,
            state.EndTs,
            state.Options,
            collector.ThreadNames,
            collector.DroppedEvents,
            collector.DroppedChunks,
            collector.SampledOutEvents,
            collector.WasOverflow);
    }

    public Scope Scope(int id)
    {
        if (!IsRunning) return new Scope(id, null, active: false);
        Write(id, TraceEventKind.Begin, 0, 0);
        return new Scope(id, this, active: true);
    }

    internal void EndScope(int id) => EndImpl(id);

    private void EndImpl(int id)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.End, 0, 0);
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
            return new FlowScope(id, 0, active: false, this);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowScope(id, flowId, active: true, this);
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

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ThreadWriter AcquireWriter(ProfilingState state)
    {
        var writer = state.GetWriter();
        _cachedWriter = writer;
        _cachedSessionId = state.Id;
        return writer;
    }
}
