using EmberTrace.Tracing;

namespace EmberTrace.Flow;

public sealed class FlowHandle
{
    private readonly Profiler _profiler;
    private int _ended;

    internal FlowHandle(int id, long flowId, Profiler profiler)
    {
        Id = id;
        FlowId = flowId;
        _profiler = profiler;
    }

    public int Id { get; }

    public long FlowId { get; }

    public bool IsValid => FlowId != 0;

    public void Step()
    {
        if (!IsValid) return;
        if (Volatile.Read(ref _ended) != 0) return;
        _profiler.FlowStep(Id, FlowId);
    }

    public bool TryEnd()
    {
        if (!IsValid) return false;
        if (Interlocked.Exchange(ref _ended, 1) != 0) return false;
        _profiler.FlowEnd(Id, FlowId);
        return true;
    }

    public void End()
    {
        TryEnd();
    }
}