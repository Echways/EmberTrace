using System;

namespace EmberTrace.Public;

public readonly ref struct FlowScope
{
    private readonly int _id;
    private readonly long _flowId;
    private readonly bool _active;

    public long FlowId => _flowId;

    internal FlowScope(int id, long flowId, bool active)
    {
        _id = id;
        _flowId = flowId;
        _active = active;
    }

    public void Step()
    {
        if (!_active) return;
        Profiler.FlowStep(_id, _flowId);
    }

    public void Dispose()
    {
        if (!_active) return;
        Profiler.FlowEnd(_id, _flowId);
    }
}