using System;
using System.Threading;
using EmberTrace.Internal.Buffering;
using EmberTrace.Internal.Time;

namespace EmberTrace.Public;

public static class Profiler
{
    private static int _enabled;
    private static SessionOptions? _options;

    private static long _startTs;
    private static long _endTs;

    private static SessionCollector? _collector;
    private static ChunkPool? _pool;

    [ThreadStatic] private static ThreadWriter? _writer;

    private static long _nextFlowId;

    public static bool IsRunning => Volatile.Read(ref _enabled) == 1;

    public static void Start(SessionOptions? options = null)
    {
        if (Interlocked.Exchange(ref _enabled, 1) == 1)
            throw new InvalidOperationException("Profiler session already running.");

        _options = options ?? new SessionOptions();
        _collector = new SessionCollector();
        _pool = new ChunkPool(Math.Max(1024, _options.ChunkCapacity));
        _writer = null;

        _startTs = Timestamp.Now();
        _endTs = 0;

        _nextFlowId = 0;
    }

    public static TraceSession Stop()
    {
        if (Interlocked.Exchange(ref _enabled, 0) == 0)
            throw new InvalidOperationException("Profiler session is not running.");

        _endTs = Timestamp.Now();

        var collector = _collector;
        var options = _options ?? new SessionOptions();

        if (collector is not null)
        {
            collector.Close();
            var writers = collector.Writers;
            for (int i = 0; i < writers.Count; i++)
                writers[i].Close();
        }

        _collector = null;
        _pool = null;
        _writer = null;

        return new TraceSession(collector?.Chunks ?? Array.Empty<Chunk>(), _startTs, _endTs, options);
    }

    public static Scope Scope(int id)
    {
        if (!IsRunning) return new Scope(id, active: false);
        Write(id, TraceEventKind.Begin, 0);
        return new Scope(id, active: true);
    }

    internal static void End(int id)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.End, 0);
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
        Write(id, TraceEventKind.FlowStart, flowId);
    }

    public static void FlowStep(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowStep, flowId);
    }

    public static void FlowEnd(int id, long flowId)
    {
        if (!IsRunning) return;
        if (flowId == 0) return;
        Write(id, TraceEventKind.FlowEnd, flowId);
    }
    
    public static long FlowStartNew(int id)
    {
        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return flowId;
    }

    public static FlowScope Flow(int id)
    {
        if (!IsRunning)
            return new FlowScope(id, 0, active: false);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowScope(id, flowId, active: true);
    }
    
    public static FlowPair FlowStartNewPair(int id)
    {
        if (!IsRunning)
            return new FlowPair(id, 0);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowPair(id, flowId);
    }

    public static void FlowEnd(FlowPair pair)
    {
        pair.End();
    }
    
    public static FlowHandle FlowStartNewHandle(int id)
    {
        if (!IsRunning)
            return new FlowHandle(id, 0);

        var flowId = NewFlowId();
        FlowStart(id, flowId);
        return new FlowHandle(id, flowId);
    }
    
    private static void Write(int id, TraceEventKind kind, long flowId)
    {
        var collector = _collector;
        var pool = _pool;
        if (collector is null || pool is null || collector.IsClosed)
            return;

        var w = _writer;
        if (w is null || w.IsClosed)
        {
            w = new ThreadWriter(collector, pool);
            collector.RegisterWriter(w);
            _writer = w;
        }

        w.Write(id, kind, flowId);
    }
}
