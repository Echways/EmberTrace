using System;
using System.Threading;
using EmberTrace.Tracing;

namespace EmberTrace.Flow;

public struct FlowScope : IDisposable
{
    private readonly int _id;
    private readonly long _flowId;
    private readonly Profiler _profiler;
    private int _ended;

    public int Id => _id;
    public long FlowId => _flowId;
    public bool IsValid => _flowId != 0;

    internal FlowScope(int id, long flowId, bool active, Profiler profiler)
    {
        _id = id;
        _flowId = flowId;
        _profiler = profiler;
        _ended = active ? 0 : 1;
    }

    public void Step()
    {
        if (!IsValid) return;
        if (Volatile.Read(ref _ended) != 0) return;
        _profiler.FlowStep(_id, _flowId);
    }

    public FlowHandle ToHandle()
    {
        if (!IsValid) return new FlowHandle(_id, 0, _profiler);
        if (Interlocked.Exchange(ref _ended, 1) != 0)
            return new FlowHandle(_id, 0, _profiler);
        return new FlowHandle(_id, _flowId, _profiler);
    }

    public void Dispose()
    {
        if (!IsValid) return;
        if (Interlocked.Exchange(ref _ended, 1) != 0) return;
        _profiler.FlowEnd(_id, _flowId);
    }
}
