using System;
using System.Collections.Generic;
using System.Threading;
using EmberTrace.Internal;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;
using EmberTrace.Flow;
using EmberTrace.Metadata;
using EmberTrace.Sessions;

namespace EmberTrace.Tracing;

internal static class Profiler
{
    private static int _enabled;
    private static ProfilingState? _state;
    private static long _nextFlowId;

    [ThreadStatic] private static ThreadWriter? _writer;
    [ThreadStatic] private static int _writerVersion;

    private static int _sessionVersion;

    public static bool IsRunning => Volatile.Read(ref _enabled) == 1;

    public static void Start(SessionOptions? options = null)
    {
        if (Interlocked.Exchange(ref _enabled, 1) == 1)
            throw new InvalidOperationException("Profiler session already running.");

        Interlocked.Increment(ref _sessionVersion);

        var opts = options ?? new SessionOptions();
#if DEBUG
        Tracer.EnableRuntimeMetadata();
#else
        if (opts.EnableRuntimeMetadata)
            Tracer.EnableRuntimeMetadata();
#endif
        var chunkCapacity = Math.Max(1024, opts.ChunkCapacity);
        var pool = new ChunkPool(chunkCapacity);
        var collector = new SessionCollector(opts, pool, chunkCapacity);
        _writer = null;
        _nextFlowId = 0;

        var meta = TraceMetadata.CreateDefault();
        CategoryFilter? categoryFilter = null;
        if ((opts.EnabledCategoryIds?.Length ?? 0) > 0 || (opts.DisabledCategoryIds?.Length ?? 0) > 0)
            categoryFilter = new CategoryFilter(meta, opts.EnabledCategoryIds, opts.DisabledCategoryIds);

        var sampling = new SamplingPolicy(opts.SampleEveryNGlobal, opts.SampleEveryNById, opts.MaxEventsPerSecond);

        _state = new ProfilingState(opts, pool, collector, categoryFilter, sampling, Timestamp.Now());
    }

    public static TraceSession Stop()
    {
        if (Interlocked.Exchange(ref _enabled, 0) == 0)
            throw new InvalidOperationException("Profiler session is not running.");

        var state = _state;
        _state = null;
        _writer = null;

        if (state is null)
            return new TraceSession(Array.Empty<Chunk>(), 0, 0, new SessionOptions(), new Dictionary<int, string>(), 0, 0, 0, false);

        state.EndTs = Timestamp.Now();

        var collector = state.Collector;
        var pool = state.Pool;
        IReadOnlyList<Chunk> sessionChunks = Array.Empty<Chunk>();
        var droppedEvents = 0L;
        var droppedChunks = 0L;
        var sampledOutEvents = 0L;
        var wasOverflow = false;
        IReadOnlyDictionary<int, string> threadNames = new Dictionary<int, string>();

        collector.Close();
        var writers = collector.Writers;
        foreach (var t in writers)
            t.CloseAndDetach();

        var chunks = collector.Chunks;
        sessionChunks = chunks;
        droppedEvents = collector.DroppedEvents;
        droppedChunks = collector.DroppedChunks;
        sampledOutEvents = collector.SampledOutEvents;
        wasOverflow = collector.WasOverflow;
        threadNames = collector.ThreadNames;

        if (chunks.Count > 0)
        {
            var snapshot = new Chunk[chunks.Count];
            for (int i = 0; i < chunks.Count; i++)
            {
                var source = chunks[i];
                var copy = new Chunk(source.Count);
                if (source.Count > 0)
                    Array.Copy(source.Events, copy.Events, source.Count);
                copy.Count = source.Count;
                snapshot[i] = copy;

                pool.Return(source);
            }

            sessionChunks = snapshot;
        }

        return new TraceSession(
            sessionChunks,
            state.StartTs,
            state.EndTs,
            state.Options,
            threadNames,
            droppedEvents,
            droppedChunks,
            sampledOutEvents,
            wasOverflow);
    }

    public static Scope Scope(int id)
    {
        if (!IsRunning) return new Scope(id, active: false);
        Write(id, TraceEventKind.Begin, 0, 0);
        return new Scope(id, active: true);
    }

    internal static void End(int id)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.End, 0, 0);
    }

    public static long NewFlowId()
    {
        var x = Interlocked.Increment(ref _nextFlowId);
        return x == 0 ? Interlocked.Increment(ref _nextFlowId) : x;
    }

    public static void FlowStart(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowStart, flowId, 0);
    }

    public static void FlowStep(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowStep, flowId, 0);
    }

    public static void FlowEnd(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowEnd, flowId, 0);
    }

    public static long FlowStartNew(int id)
    {
        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return flowId;
    }

    public static void Instant(int id)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.Instant, 0, 0);
    }

    public static void Counter(int id, long value)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.Counter, 0, value);
    }

    public static FlowScope Flow(int id)
    {
        if (!IsRunning)
            return new FlowScope(id, 0, active: false);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowScope(id, flowId, active: true);
    }
    public static FlowHandle FlowStartNewHandle(int id)
    {
        if (!IsRunning)
            return new FlowHandle(id, 0);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowHandle(id, flowId);
    }

    private static void Write(int id, TraceEventKind kind, long flowId, long value)
    {
        var state = _state;
        if (state is null || state.Collector.IsClosed)
            return;

        var filter = state.CategoryFilter;
        if (filter is not null && !filter.Allows(id))
            return;

        var version = Volatile.Read(ref _sessionVersion);
        if (_writerVersion != version)
        {
            _writer?.CloseAndDetach();
            _writer = null;
            _writerVersion = version;
        }

        var collector = state.Collector;
        var w = _writer;
        if (w is null || w.IsClosed)
        {
            w = new ThreadWriter(collector, state.Sampling);
            collector.RegisterWriter(w);
            _writer = w;
        }

        w.Write(id, kind, flowId, value);
    }

    public static void EndScope(int id) => End(id);

}
