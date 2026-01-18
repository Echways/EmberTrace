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
        Write(id, TraceEventKind.Begin);
        return new Scope(id, active: true);
    }

    internal static void End(int id)
    {
        if (!IsRunning) return;
        Write(id, TraceEventKind.End);
    }

    private static void Write(int id, TraceEventKind kind)
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

        w.Write(id, kind);
    }
}
