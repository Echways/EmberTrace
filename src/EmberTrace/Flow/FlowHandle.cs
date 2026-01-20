using System.Threading;
using EmberTrace.Tracing;

namespace EmberTrace.Flow;

public sealed class FlowHandle
{
    private readonly int _id;
    private readonly long _flowId;
    private int _ended;

    public int Id => _id;
    public long FlowId => _flowId;
    public bool IsValid => _flowId != 0;

    internal FlowHandle(int id, long flowId)
    {
        _id = id;
        _flowId = flowId;
    }

    public void Step()
    {
        if (!IsValid) return;
        if (Volatile.Read(ref _ended) != 0) return;
        Profiler.FlowStep(_id, _flowId);
    }

    public bool TryEnd()
    {
        if (!IsValid) return false;
        if (Interlocked.Exchange(ref _ended, 1) != 0) return false;
        Profiler.FlowEnd(_id, _flowId);
        return true;
    }

    public void End()
    {
        TryEnd();
    }
}