using EmberTrace.Tracing;

namespace EmberTrace.Flow;

internal readonly struct FlowPair
{
    public readonly int Id;
    public readonly long FlowId;
    private readonly Profiler _profiler;

    public FlowPair(int id, long flowId, Profiler profiler)
    {
        Id = id;
        FlowId = flowId;
        _profiler = profiler;
    }

    public bool IsValid => FlowId != 0;

    public void Step()
    {
        if (!IsValid) return;
        _profiler.FlowStep(Id, FlowId);
    }

    public void End()
    {
        if (!IsValid) return;
        _profiler.FlowEnd(Id, FlowId);
    }
}
