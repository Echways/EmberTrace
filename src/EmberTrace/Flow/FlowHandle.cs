using System.Threading;
using EmberTrace.Tracing;

namespace EmberTrace.Flow;

public sealed class FlowHandle
{
    private readonly int _id;
    private readonly long _flowId;
    private readonly Profiler _profiler;
    private int _ended;

    public int Id => _id;
    public long FlowId => _flowId;
    public bool IsValid => _flowId != 0;

    internal FlowHandle(int id, long flowId, Profiler profiler)
    {
        _id = id;
        _flowId = flowId;
        _profiler = profiler;
    }

    public void Step()
    {
        if (!IsValid) return;
        if (Volatile.Read(ref _ended) != 0) return;
        _profiler.FlowStep(_id, _flowId);
    }

    public bool TryEnd()
    {
        if (!IsValid) return false;
        if (Interlocked.Exchange(ref _ended, 1) != 0) return false;
        _profiler.FlowEnd(_id, _flowId);
        return true;
    }

    public void End()
    {
        TryEnd();
    }
}
