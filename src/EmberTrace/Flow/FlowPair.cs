using EmberTrace.Tracing;

namespace EmberTrace.Flow;

internal readonly struct FlowPair
{
    public readonly int Id;
    public readonly long FlowId;

    public FlowPair(int id, long flowId)
    {
        Id = id;
        FlowId = flowId;
    }

    public bool IsValid => FlowId != 0;

    public void Step()
    {
        if (!IsValid) return;
        Profiler.FlowStep(Id, FlowId);
    }

    public void End()
    {
        if (!IsValid) return;
        Profiler.FlowEnd(Id, FlowId);
    }
}